// Project: sboxgamejam3
// File:    AICarDriver.Telemetry.cs
// Author:  cseliot
// Created: 2026.10.05
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sandbox;

/// <summary>
/// Play-session telemetry for <see cref="AICarDriver"/>: a sample stream, an event log with world
/// context for every incident (blocks, reverses, collisions, teleports, flips), per-leg results and
/// run totals, segmented into EPOCHS, one per distinct (DriverVersion, tuning) configuration, so a
/// local LLM agent can compare before/after inside ONE continuous play session (the live feedback
/// loop never stops the game). Each car's files are handed to <see cref="AICarTelemetrySink"/>,
/// which writes them to FileSystem.Data and lets the editor exporter (Editor/AICarTelemetryExporter.cs)
/// mirror them to PROJECT/.aicar-telemetry/ while play runs. See AI-Car-Feedback-Loop.md.
/// </summary>
public sealed partial class AICarDriver : Component.ICollisionListener
{
	/// <summary>
	/// Bumped with every tuning or logic change made through the feedback loop, so each recorded
	/// run says which driver produced it.
	/// </summary>
	public const string DriverVersion = "v18b-nogo-fixes";

	// ------------------------------------------------------------------ Telemetry

	/// <summary>Record this car's driving for the feedback loop (host/solo only).</summary>
	[Property, Group( "Telemetry" )] public bool RecordTelemetry { get; set; } = true;

	/// <summary>Samples per second written to samples.csv.</summary>
	[Property, Group( "Telemetry" ), Range( 1f, 20f )] public float TelemetrySampleRate { get; set; } = 2f;

	/// <summary>Seconds between snapshots handed to the sink; the live agent polls these files during play.</summary>
	[Property, Group( "Telemetry" )] public float TelemetryFlushInterval { get; set; } = 5f;

	/// <summary>
	/// Extra copies of this car spawned at random navmesh spots when play starts (only the first
	/// AI car in a scene spawns them). More cars = more data per play session and car-vs-car traffic.
	/// </summary>
	[Property, Group( "Telemetry" ), Range( 0, 8 )] public int ExtraTestCars { get; set; } = 0;

	/// <summary>
	/// Apply the feedback loop's code-side tuning (AICarDriver.Tuning.cs) over the inspector values
	/// at start. Turn off to drive purely on the inspector / scene values.
	/// </summary>
	[Property, Group( "Telemetry" )] public bool UseDevTuning { get; set; } = true;

	// ------------------------------------------------------------------ Telemetry internals (session-wide)

	private static Scene _sessionScene;
	private static string _sessionId = "";
	private static int _sessionCarCount;
	private static bool _sessionNavSampled;
	private static Scene _clonesSpawnedForScene;

	private bool _telemetryActive;
	private bool _teleportPending;
	private bool _stuckTrigger;
	private bool _tiltTrigger;
	private string _carId = "";
	private TimeSince _sinceTelemetryStart;
	private TimeSince _sinceSample;
	private TimeSince _sinceFlush;
	private Vector3 _lastTelemetryPosition;

	// Episode continuity (not totals): these latch across epoch splits on purpose.
	private bool _flipped;
	private TimeSince _sinceUpright;
	private bool _offNav;
	private float _lastOffNavDistance;
	private TimeSince _sinceNavCheck = 10f;
	private TimeSince _sinceCollisionEvent = 10f;

	// Epoch accumulator. All counters/buffers below are scoped to the OPEN epoch; closed epochs
	// keep only their compact epochs.csv row, so memory stays bounded over multi-hour sessions.
	// NOTE: hotload leaves these default(T) on live instances - every use null-checks / lazily
	// re-initializes (see TelemetryTickHelper and CloseEpochHelper).
	private Epoch _epoch;
	private List<string> _epochRows;            // finalized csv rows of closed epochs, in order
	private string _lastEpochSummary = "";      // one-line summary of the most recently closed epoch

	private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	private const string SamplesHeader = "t,x,y,z,yaw,speed,fwd,target,throttle,brake,steer,state,aggr,wp,pathdev,pathang,avoidang,fclear,lclear,rclear,offnav,upz,fhit,aim,lflank,rflank";
	private const string LegsHeader = "start_t,duration,outcome,planned,driven,reverses,collisions,avg_speed";
	private const string EpochsHeader = "epoch,key,version,tuning_revision,start_t,duration,closed,distance,arrived,legs,blocked,reverses,collisions,collisions_static,collisions_dynamic,collisions_hard,teleports,flips,offnav_time,idle_time,steer_sign_flips";

	/// <summary>Everything recorded between two configuration changes (one epoch).</summary>
	private sealed class Epoch
	{
		public int Index;                       // per-car, 0-based, zero-padded to e{NNN} on disk
		public string Key = "";                 // $"{DriverVersion}|{TuningRevision}"
		public string TuningRevision = "none";
		public string AppliedTuning = "{}";     // AppliedTuningJsonHelper() at open time
		public float StartT;                    // seconds since SESSION start
		public float EndT;                      // set at close
		public bool Closed;

		public readonly StringBuilder Samples = new();
		public readonly StringBuilder Events = new();
		public readonly StringBuilder Legs = new();
		public int SampleCount;
		public int EventCount;
		public int LegCount;

		// Totals.
		public float Distance;
		public readonly Dictionary<DriveState, float> StateTime = new();
		public readonly Dictionary<string, int> Counters = new();
		public float OffNavTime;
		public float FlippedTime;
		public float MaxImpactSpeed;
		public int SteerSignFlips;
		public float LastSteerSign;
		public float SpeedSum;
		public int SpeedSamples;

		// Current leg (destination) inside this epoch.
		public bool LegActive;
		public float LegStartTime;
		public float LegPlannedLength;
		public float LegDriven;
		public int LegReverses;
		public int LegCollisions;

		public int Count( string key ) => Counters.TryGetValue( key, out int v ) ? v : 0;
	}

	// ================================================================== Lifecycle

	private void TelemetryStartHelper()
	{
		if ( !RecordTelemetry ) return;

		EnsureSessionHelper();
		_carId = $"car{_sessionCarCount++}";
		_telemetryActive = true;
		_sinceTelemetryStart = 0f;
		_sinceSample = 0f;
		_sinceFlush = 0f;
		_lastTelemetryPosition = WorldPosition;

		// OnStart ran TuningTickHelper() first, so epoch 000 already carries the applied tuning.
		OpenEpochHelper();

		TelemetryEventHelper( "Start", $"name={GameObject.Name} version={DriverVersion} clone={_teleportPending}" );
	}

	/// <summary>
	/// (Re)initialize the session identity: at play start, and again after a hotload wiped the
	/// statics mid-play. Only the car that (re)creates the session writes current_session.txt.
	/// </summary>
	private void EnsureSessionHelper()
	{
		if ( _sessionScene == Scene && !string.IsNullOrEmpty( _sessionId ) ) return;

		_sessionScene = Scene;
		_sessionId = DateTime.Now.ToString( "yyyyMMdd-HHmmss", Inv );
		_sessionCarCount = 0;
		_sessionNavSampled = false;
		AICarTelemetrySink.Put( "aicar/current_session.txt", $"{_sessionId}\n" );
	}

	private string EpochKeyHelper() => $"{DriverVersion}|{TuningRevision ?? "none"}";

	/// <summary>
	/// Open a fresh epoch: fresh buffers and totals, starting now on the session clock.
	/// </summary>
	private void OpenEpochHelper()
	{
		var e = new Epoch
		{
			Index = _epochRows?.Count ?? 0,
			Key = EpochKeyHelper(),
			TuningRevision = TuningRevision ?? "none",
			AppliedTuning = AppliedTuningJsonHelper() ?? "{}",
			StartT = _sinceTelemetryStart,
		};
		e.Samples.AppendLine( SamplesHeader );
		_epoch = e;

		Log.Info( $"AICarTelemetry {_carId}: epoch {e.Index:000} open [{e.Key}]" );
		TelemetryEventHelper( "EpochStart", $"key={e.Key} tuning={e.AppliedTuning}" );
	}

	/// <summary>
	/// Called from OnFixedUpdate when TuningTickHelper() says the applied configuration just changed.
	/// Closes the open epoch (leg closed as "epoch-split", files final with closed=true, buffers
	/// dropped) and opens the new one, restarting the leg record for the current path.
	/// No-ops when the epoch already has this exact key: the key's tuning part carries a hash of the
	/// applied values, so this only skips a re-apply that landed on identical values (for example a
	/// tuning file rewritten with the same content). After a hotload (play keeps running,
	/// instance fields and statics are default(T) again) this restarts recording: OnStart will not
	/// re-run, and this is the first tick-driven hook after the reload - the epoch key carries the
	/// new DriverVersion, which is what the loop confirms before trusting the data.
	/// </summary>
	private void TelemetryNewEpochHelper()
	{
		if ( !RecordTelemetry ) return;

		if ( !_telemetryActive )
		{
			TelemetryStartHelper();
			return;
		}

		string key = EpochKeyHelper();
		if ( _epoch is not null && _epoch.Key == key ) return;

		bool restartLeg = _epoch is not null && _epoch.LegActive;

		EnsureSessionHelper();
		CloseEpochHelper( false );
		OpenEpochHelper();

		if ( restartLeg ) TelemetryLegStartHelper();
	}

	/// <summary>
	/// Finish the open epoch: close its leg, write its files one last time (closed=true), keep only
	/// its csv row + one-line summary, drop the buffers.
	/// </summary>
	private void CloseEpochHelper( bool final )
	{
		if ( _epoch is null ) return;
		Epoch e = _epoch;

		if ( e.LegActive ) TelemetryLegEndHelper( final ? "unfinished" : "epoch-split" );
		if ( final ) TelemetryEventHelper( "End", "" );

		e.Closed = true;
		e.EndT = _sinceTelemetryStart;

		WriteEpochFilesHelper( e, final );
		(_epochRows ??= new()).Add( EpochRowHelper( e ) );
		_lastEpochSummary = BuildOneLineSummaryHelper( e );
		_epoch = null;
	}

	private void TelemetryTickHelper( Vector3 position, float forwardSpeed )
	{
		if ( !_telemetryActive ) return;

		// Hotload safety: the epoch field is default(T) on live instances - reopen one (the
		// EnsureSessionHelper then keeps the file paths valid if the statics were wiped too).
		if ( _epoch is null )
		{
			EnsureSessionHelper();
			OpenEpochHelper();
		}

		Epoch e = _epoch;
		float dt = Time.Delta;
		float step = FlatDistance( position, _lastTelemetryPosition );
		// Teleports jump; anything faster than ~5x cruise in one tick is not driving.
		if ( step < MathF.Max( CruiseSpeed, 500f ) * 5f * MathF.Max( dt, 0.001f ) )
		{
			e.Distance += step;
			if ( e.LegActive ) e.LegDriven += step;
		}
		_lastTelemetryPosition = position;

		e.StateTime[State] = (e.StateTime.TryGetValue( State, out float st ) ? st : 0f) + dt;

		if ( State == DriveState.Driving )
		{
			e.SpeedSum += MathF.Abs( forwardSpeed );
			e.SpeedSamples++;

			// Weaving: steering command sign flips while driving with a meaningful lock.
			if ( MathF.Abs( _steer ) > 0.25f )
			{
				float sign = MathF.Sign( _steer );
				if ( e.LastSteerSign != 0f && sign != e.LastSteerSign ) e.SteerSignFlips++;
				e.LastSteerSign = sign;
			}
		}

		// Flip detection: on the side or roof for more than 1.5 s.
		float upZ = WorldRotation.Up.z;
		if ( upZ > 0.5f )
		{
			_sinceUpright = 0f;
			_flipped = false;
		}
		else
		{
			e.FlippedTime += dt;
			if ( !_flipped && _sinceUpright > 1.5f )
			{
				_flipped = true;
				CountHelper( "flips" );
				TelemetryEventHelper( "Flipped", $"upz={F( upZ, 2 )} {ScanSurroundingsHelper()}" );
			}
		}

		// Off the navmesh (road): checked 5x a second.
		if ( _sinceNavCheck > 0.2f )
		{
			_sinceNavCheck = 0f;
			_lastOffNavDistance = GetOffNavDistanceHelper( position );
			bool off = _lastOffNavDistance > 60f;
			if ( off && !_offNav )
			{
				CountHelper( "offnav_episodes" );
				TelemetryEventHelper( "OffNav", $"dist={F( _lastOffNavDistance, 0 )} {ScanSurroundingsHelper()}" );
			}
			else if ( !off && _offNav )
			{
				TelemetryEventHelper( "OnNav", "" );
			}
			_offNav = off;
		}
		if ( _offNav ) e.OffNavTime += dt;

		if ( TelemetrySampleRate > 0f && _sinceSample >= 1f / TelemetrySampleRate )
		{
			_sinceSample = 0f;
			WriteSampleHelper( position, forwardSpeed, upZ );
		}

		if ( _sinceFlush > MathF.Max( TelemetryFlushInterval, 2f ) )
			TelemetryFlushHelper( false );

		if ( !_sessionNavSampled && Scene.NavMesh is { IsEnabled: true } )
			SampleNavMeshHelper();
	}

	private void WriteSampleHelper( Vector3 position, float forwardSpeed, float upZ )
	{
		GetFrontClearanceHelper( out float forwardClear, out _, out float leftClear, out float rightClear );

		string hit = "";
		if ( _frontHitName is not null )
		{
			for ( int i = 0; i < _frontHitName.Length; i++ )
			{
				if ( _frontHitDistance[i] < _frontLength[i] && !string.IsNullOrEmpty( _frontHitName[i] ) )
				{
					hit = _frontHitName[i];
					break;
				}
			}
		}

		bool driving = State == DriveState.Driving;
		var sb = _epoch.Samples;
		sb.Append( F( _sinceTelemetryStart, 2 ) ).Append( ',' )   // t stays SECONDS SINCE SESSION START
			.Append( F( position.x, 0 ) ).Append( ',' )
			.Append( F( position.y, 0 ) ).Append( ',' )
			.Append( F( position.z, 0 ) ).Append( ',' )
			.Append( F( WorldRotation.Yaw(), 1 ) ).Append( ',' )
			.Append( F( _body.Velocity.WithZ( 0f ).Length, 0 ) ).Append( ',' )
			.Append( F( forwardSpeed, 0 ) ).Append( ',' )
			.Append( F( driving ? _lastTargetSpeed : 0f, 0 ) ).Append( ',' )
			.Append( F( _lastThrottle, 2 ) ).Append( ',' )
			.Append( _lastBrake ? '1' : '0' ).Append( ',' )
			.Append( F( _steer, 2 ) ).Append( ',' )
			.Append( State ).Append( ',' )
			.Append( F( Aggression, 2 ) ).Append( ',' )
			.Append( _waypointIndex ).Append( ',' )
			.Append( F( driving ? _lastDistanceFromPath : 0f, 0 ) ).Append( ',' )
			.Append( F( driving ? _lastPathAngle : 0f, 1 ) ).Append( ',' )
			.Append( F( driving ? _lastAvoidAngle : 0f, 1 ) ).Append( ',' )
			.Append( F( Clamp9999( forwardClear ), 0 ) ).Append( ',' )
			.Append( F( Clamp9999( leftClear ), 0 ) ).Append( ',' )
			.Append( F( Clamp9999( rightClear ), 0 ) ).Append( ',' )
			.Append( F( _lastOffNavDistance, 0 ) ).Append( ',' )
			.Append( F( upZ, 2 ) ).Append( ',' )
			.Append( Csv( hit ) ).Append( ',' )
			.Append( _aimingAtCorner ? '1' : '0' ).Append( ',' )
			// v16: front-flank gaps. Probed only in the normal Driving steer branch; outside it
			// (Blocked/Reversing/Idle/pivot) report the probe length, meaning "no flank info".
			.Append( F( Clamp9999( driving ? _flankLeftGap : MathF.Max( EffectiveFlankProbeLength, 0f ) ), 0 ) ).Append( ',' )
			.Append( F( Clamp9999( driving ? _flankRightGap : MathF.Max( EffectiveFlankProbeLength, 0f ) ), 0 ) ).AppendLine();
		_epoch.SampleCount++;
	}

	// ================================================================== Hooks from the driver

	private void TelemetryStateChangeHelper( DriveState from, DriveState to )
	{
		if ( !_telemetryActive || _epoch is null || from == to ) return;

		if ( to == DriveState.Blocked )
		{
			CountHelper( "blocked" );
			TelemetryEventHelper( "Blocked", $"{WhiskerSummaryHelper()} {ScanSurroundingsHelper()}" );
		}
		else if ( to == DriveState.Idle )
		{
			CountHelper( "idle_entries" );
			TelemetryEventHelper( "Idle", $"from={from}" );
		}
		else if ( from == DriveState.Blocked && to == DriveState.Driving )
		{
			CountHelper( "blocked_cleared" );
			TelemetryEventHelper( "BlockCleared", $"waited={F( _sinceStateChange, 1 )}" );
		}
		else if ( from == DriveState.Reversing )
		{
			TelemetryEventHelper( "ReverseEnd", $"to={to} after={F( _sinceStateChange, 1 )}" );
		}
	}

	private void TelemetryReverseHelper( float leftClear, float rightClear )
	{
		// v15 review fix: read and clear the trigger latches before the telemetry-off early return,
		// or a reverse taken with telemetry off leaves its latch set and mislabels the next one.
		string trigger = _stuckTrigger ? "stuck" : _tiltTrigger ? "tilt" : _reverseReason == "turn" ? "turn" : (State == DriveState.Blocked ? "blocked" : "other");
		_stuckTrigger = false;
		_tiltTrigger = false;

		if ( !_telemetryActive || _epoch is null ) return;

		CountHelper( "reverses" );
		CountHelper( $"reverses_{trigger}" );
		if ( _epoch.LegActive ) _epoch.LegReverses++;

		TelemetryEventHelper( "Reverse",
			$"trigger={trigger} count={_reverseCount} nose={(_reverseSteer < 0f ? "left" : "right")} why={_reverseReason} " +
			$"l={F( Clamp9999( leftClear ), 0 )} r={F( Clamp9999( rightClear ), 0 )} {WhiskerSummaryHelper()} {ScanSurroundingsHelper()}" );
	}

	private void TelemetryLegStartHelper()
	{
		if ( !_telemetryActive || _epoch is null ) return;

		float length = 0f;
		for ( int i = 1; i < _path.Count; i++ )
			length += FlatDistance( _path[i - 1], _path[i] );

		_epoch.LegActive = true;
		_epoch.LegStartTime = _sinceTelemetryStart;
		_epoch.LegPlannedLength = length;
		_epoch.LegDriven = 0f;
		_epoch.LegReverses = 0;
		_epoch.LegCollisions = 0;

		CountHelper( "legs_started" );
		TelemetryEventHelper( "LegStart", $"dest={Fmt( Destination )} planned={F( length, 0 )} points={_path.Count} " +
			$"minw={F( Clamp9999( _legMinWidth ), 0 )} minw_at={F( _legMinWidthAt.x, 0 )},{F( _legMinWidthAt.y, 0 )} " +
			$"rejected={_legRoutesRejected} " +
			$"nogo_rejected={_legNogoRejected} " +
			$"rej_minw={F( Clamp9999( _legRoutesRejected > 0 ? _rejMinWidth : -1f ), 0 )} " +
			$"rej_minw_at={F( _legRoutesRejected > 0 ? _rejMinWidthAt.x : 0f, 0 )},{F( _legRoutesRejected > 0 ? _rejMinWidthAt.y : 0f, 0 )} " +
			$"path={FmtPath( _path )}" );
	}

	private void TelemetryLegEndHelper( string outcome )
	{
		if ( !_telemetryActive || _epoch is null || !_epoch.LegActive ) return;
		Epoch e = _epoch;
		e.LegActive = false;

		float duration = _sinceTelemetryStart - e.LegStartTime;
		CountHelper( $"legs_{outcome}" );

		if ( e.LegCount == 0 )
			e.Legs.AppendLine( LegsHeader );
		e.LegCount++;

		e.Legs.Append( F( e.LegStartTime, 1 ) ).Append( ',' )
			.Append( F( duration, 1 ) ).Append( ',' )
			.Append( outcome ).Append( ',' )
			.Append( F( e.LegPlannedLength, 0 ) ).Append( ',' )
			.Append( F( e.LegDriven, 0 ) ).Append( ',' )
			.Append( e.LegReverses ).Append( ',' )
			.Append( e.LegCollisions ).Append( ',' )
			.Append( F( duration > 0.01f ? e.LegDriven / duration : 0f, 0 ) ).AppendLine();

		TelemetryEventHelper( "LegEnd", $"outcome={outcome} duration={F( duration, 1 )} planned={F( e.LegPlannedLength, 0 )} driven={F( e.LegDriven, 0 )}" );
	}

	/// <summary>Records body hits that are not the ground (curbs, walls, poles, cars, players).</summary>
	public void OnCollisionStart( Collision collision )
	{
		if ( !_telemetryActive || _epoch is null ) return;

		GameObject other = collision.Other.GameObject;
		if ( IsOwnPartHelper( other ) ) return;

		Vector3 normal = collision.Contact.Normal;
		if ( MathF.Abs( normal.z ) >= GroundNormalZ ) return;

		float speed = collision.Contact.Speed.Length;
		if ( speed < 30f ) return;

		bool dynamic = other.IsValid() && other.Components.Get<Rigidbody>() is { MotionEnabled: true };
		string kind = dynamic ? "dynamic" : "static";

		CountHelper( "collisions" );
		CountHelper( $"collisions_{kind}" );
		if ( speed > 200f ) CountHelper( "collisions_hard" );
		_epoch.MaxImpactSpeed = MathF.Max( _epoch.MaxImpactSpeed, speed );
		if ( _epoch.LegActive ) _epoch.LegCollisions++;

		// Scraping along a wall fires many starts; keep the event log readable.
		if ( _sinceCollisionEvent < 0.5f ) return;
		_sinceCollisionEvent = 0f;

		string name = other.IsValid() ? other.Name : "?";
		TelemetryEventHelper( "Collision",
			$"other={name} kind={kind} speed={F( speed, 0 )} at={Fmt( collision.Contact.Point )} n={F( normal.x, 2 )},{F( normal.y, 2 )} {WhiskerSummaryHelper()}" );
	}

	// ================================================================== Output

	private void TelemetryEventHelper( string type, string detail )
	{
		if ( !_telemetryActive || _epoch is null ) return;

		Vector3 p = WorldPosition;
		float speed = _body.IsValid() ? _body.Velocity.WithZ( 0f ).Length : 0f;

		_epoch.Events.Append( "{\"t\":" ).Append( F( _sinceTelemetryStart, 2 ) )
			.Append( ",\"type\":\"" ).Append( type ).Append( '"' )
			.Append( ",\"x\":" ).Append( F( p.x, 0 ) )
			.Append( ",\"y\":" ).Append( F( p.y, 0 ) )
			.Append( ",\"z\":" ).Append( F( p.z, 0 ) )
			.Append( ",\"yaw\":" ).Append( F( WorldRotation.Yaw(), 1 ) )
			.Append( ",\"speed\":" ).Append( F( speed, 0 ) )
			.Append( ",\"state\":\"" ).Append( State ).Append( '"' )
			.Append( ",\"aggr\":" ).Append( F( Aggression, 2 ) )
			.Append( ",\"detail\":\"" ).Append( JsonEscape( detail ) ).Append( '"' )
			.Append( ",\"epoch\":" ).Append( _epoch.Index ).Append( "}" ).AppendLine();
		_epoch.EventCount++;
	}

	private void TelemetryFlushHelper( bool final )
	{
		if ( !_telemetryActive ) return;
		_sinceFlush = 0f;

		// Runs from OnDestroy while the play scene tears down: never let telemetry throw there.
		try
		{
			if ( final )
			{
				CloseEpochHelper( true );
				WriteEpochsCsvHelper();
				Log.Info( $"AICarTelemetry {_sessionId}/{_carId} {DriverVersion}: {(_epochRows?.Count ?? 0)} epochs, last: {_lastEpochSummary}" );
			}
			else if ( _epoch is not null )
			{
				WriteEpochFilesHelper( _epoch, false );
				WriteEpochsCsvHelper();
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"AICarTelemetry {_carId}: flush failed: {e.Message}" );
		}

		if ( final ) _telemetryActive = false;
	}

	private void WriteEpochFilesHelper( Epoch e, bool final )
	{
		string dir = $"aicar/{_sessionId}/{_carId}/e{e.Index:000}";
		AICarTelemetrySink.Put( $"{dir}/samples.csv", e.Samples.ToString() );
		AICarTelemetrySink.Put( $"{dir}/events.jsonl", e.Events.ToString() );
		AICarTelemetrySink.Put( $"{dir}/legs.csv", e.LegCount > 0 ? e.Legs.ToString() : LegsHeader + "\n" );
		AICarTelemetrySink.Put( $"{dir}/summary.json", BuildSummaryHelper( e, final ) );
	}

	private void WriteEpochsCsvHelper()
	{
		var sb = new StringBuilder( EpochsHeader ).AppendLine();
		if ( _epochRows is not null )
		{
			foreach ( string row in _epochRows )
				sb.Append( row ).AppendLine();
		}
		if ( _epoch is not null )
			sb.Append( EpochRowHelper( _epoch ) ).AppendLine();

		AICarTelemetrySink.Put( $"aicar/{_sessionId}/{_carId}/epochs.csv", sb.ToString() );
	}

	/// <summary>One epochs.csv row for this epoch (open or closed), without the trailing newline.</summary>
	private string EpochRowHelper( Epoch e )
	{
		float duration = (e.Closed ? e.EndT : _sinceTelemetryStart) - e.StartT;
		var sb = new StringBuilder();
		sb.Append( e.Index ).Append( ',' )
			.Append( Csv( e.Key ) ).Append( ',' )
			.Append( Csv( DriverVersion ) ).Append( ',' )
			.Append( Csv( e.TuningRevision ) ).Append( ',' )
			.Append( F( e.StartT, 1 ) ).Append( ',' )
			.Append( F( duration, 1 ) ).Append( ',' )
			.Append( e.Closed ? 1 : 0 ).Append( ',' )
			.Append( F( e.Distance, 0 ) ).Append( ',' )
			.Append( e.Count( "legs_arrived" ) ).Append( ',' )
			.Append( e.Count( "legs_started" ) ).Append( ',' )
			.Append( e.Count( "blocked" ) ).Append( ',' )
			.Append( e.Count( "reverses" ) ).Append( ',' )
			.Append( e.Count( "collisions" ) ).Append( ',' )
			.Append( e.Count( "collisions_static" ) ).Append( ',' )
			.Append( e.Count( "collisions_dynamic" ) ).Append( ',' )
			.Append( e.Count( "collisions_hard" ) ).Append( ',' )
			.Append( e.Count( "teleports" ) ).Append( ',' )
			.Append( e.Count( "flips" ) ).Append( ',' )
			.Append( F( e.OffNavTime, 1 ) ).Append( ',' )
			.Append( F( e.StateTime.TryGetValue( DriveState.Idle, out float idle ) ? idle : 0f, 1 ) ).Append( ',' )
			.Append( e.SteerSignFlips );
		return sb.ToString();
	}

	private string BuildSummaryHelper( Epoch e, bool final )
	{
		float duration = (e.Closed ? e.EndT : _sinceTelemetryStart) - e.StartT;
		var sb = new StringBuilder();
		sb.Append( "{\n" );
		sb.Append( "  \"session\": \"" ).Append( _sessionId ).Append( "\",\n" );
		sb.Append( "  \"car\": \"" ).Append( _carId ).Append( "\",\n" );
		sb.Append( "  \"name\": \"" ).Append( JsonEscape( GameObject.Name ) ).Append( "\",\n" );
		sb.Append( "  \"version\": \"" ).Append( DriverVersion ).Append( "\",\n" );
		sb.Append( "  \"scene\": \"" ).Append( JsonEscape( Scene.Name ?? "" ) ).Append( "\",\n" );
		sb.Append( "  \"epoch\": ").Append( e.Index ).Append( ",\n" );
		sb.Append( "  \"key\": \"" ).Append( JsonEscape( e.Key ) ).Append( "\",\n" );
		sb.Append( "  \"tuning_revision\": \"" ).Append( JsonEscape( e.TuningRevision ) ).Append( "\",\n" );
		sb.Append( "  \"tuning\": " ).Append( string.IsNullOrWhiteSpace( e.AppliedTuning ) ? "{}" : e.AppliedTuning ).Append( ",\n" );
		sb.Append( "  \"start_t\": ").Append( F( e.StartT, 1 ) ).Append( ",\n" );
		sb.Append( "  \"closed\": ").Append( e.Closed ? "true" : "false" ).Append( ",\n" );
		sb.Append( "  \"final\": ").Append( final ? "true" : "false" ).Append( ",\n" );
		sb.Append( "  \"duration\": ").Append( F( duration, 1 ) ).Append( ",\n" );
		sb.Append( "  \"distance\": ").Append( F( e.Distance, 0 ) ).Append( ",\n" );
		sb.Append( "  \"avg_driving_speed\": ").Append( F( e.SpeedSamples > 0 ? e.SpeedSum / e.SpeedSamples : 0f, 0 ) ).Append( ",\n" );
		sb.Append( "  \"offnav_time\": ").Append( F( e.OffNavTime, 1 ) ).Append( ",\n" );
		sb.Append( "  \"flipped_time\": ").Append( F( e.FlippedTime, 1 ) ).Append( ",\n" );
		sb.Append( "  \"max_impact_speed\": ").Append( F( e.MaxImpactSpeed, 0 ) ).Append( ",\n" );
		sb.Append( "  \"steer_sign_flips\": ").Append( e.SteerSignFlips ).Append( ",\n" );

		sb.Append( "  \"state_time\": {" );
		bool first = true;
		foreach ( DriveState s in new[] { DriveState.Idle, DriveState.Driving, DriveState.Blocked, DriveState.Reversing } )
		{
			sb.Append( first ? "" : ", " ).Append( '"' ).Append( s ).Append( "\": " )
				.Append( F( e.StateTime.TryGetValue( s, out float v ) ? v : 0f, 1 ) );
			first = false;
		}
		sb.Append( "},\n" );

		sb.Append( "  \"counters\": {" );
		first = true;
		foreach ( var pair in e.Counters )
		{
			sb.Append( first ? "" : ", " ).Append( '"' ).Append( pair.Key ).Append( "\": " ).Append( pair.Value );
			first = false;
		}
		sb.Append( "},\n" );

		sb.Append( "  \"config\": {" ).Append( BuildConfigHelper() ).Append( "}\n" );
		sb.Append( "}\n" );
		return sb.ToString();
	}

	private string BuildOneLineSummaryHelper( Epoch e )
	{
		float duration = (e.Closed ? e.EndT : _sinceTelemetryStart) - e.StartT;
		return $"{F( duration, 0 )}s [{e.Key}], {F( e.Distance / 39.37f, 0 )}m driven, arrived {e.Count( "legs_arrived" )}/{e.Count( "legs_started" )}, " +
			$"blocked {e.Count( "blocked" )}, reverses {e.Count( "reverses" )}, collisions {e.Count( "collisions" )}, teleports {e.Count( "teleports" )}, " +
			$"flips {e.Count( "flips" )}, offnav {F( e.OffNavTime, 0 )}s";
	}

	/// <summary>Every driving property, so a run records exactly which tuning produced it.</summary>
	private string BuildConfigHelper()
	{
		var values = new (string, float)[]
		{
			("MinDestinationDistance", MinDestinationDistance), ("MaxDestinationDistance", MaxDestinationDistance),
			("NavMeshSnapRadius", NavMeshSnapRadius), ("ArrivalDistance", ArrivalDistance),
			("LookAheadDistance", LookAheadDistance), ("LookAheadTime", LookAheadTime),
			("OffPathDistance", OffPathDistance), ("CornerLookDistance", CornerLookDistance),
			("CruiseSpeed", CruiseSpeed), ("CornerSpeed", CornerSpeed), ("CreepSpeed", CreepSpeed),
			("ThrottleResponse", ThrottleResponse), ("BrakeMargin", BrakeMargin), ("ObstacleBrakingRate", ObstacleBrakingRate),
			("FullSteerAngle", FullSteerAngle), ("SteerResponse", SteerResponse),
			// v13 review fix: log EFFECTIVE values (hotload fallbacks applied), not the raw
			// properties - a hotloaded instance reads 0 for new properties, and the raw dump
			// recorded 0 while the car actually drove with the fallback. This is what hid the
			// stale CornerAimTraceRadius=50 during the v12b-e aim debugging.
			("SteerDeadband", EffectiveSteerDeadband), ("SteerYawDamping", EffectiveSteerYawDamping),
			("WhiskerLength", WhiskerLength), ("WhiskerLengthPerSpeed", WhiskerLengthPerSpeed),
			("SideWhiskerAngle", SideWhiskerAngle), ("SideWhiskerScale", SideWhiskerScale),
			("WhiskerHeight", WhiskerHeight), ("WhiskerThickness", WhiskerThickness), ("GroundNormalZ", GroundNormalZ),
			("AvoidSteerAngle", AvoidSteerAngle), ("StopDistance", StopDistance), ("BlockedWaitTime", BlockedWaitTime),
			("ReverseThrottle", ReverseThrottle), ("MinReverseTime", MinReverseTime), ("MaxReverseTime", MaxReverseTime),
			("RearProbeLength", RearProbeLength), ("RearStopDistance", RearStopDistance),
			("StuckSpeed", StuckSpeed), ("StuckTime", StuckTime), ("RepickAfterReverses", RepickAfterReverses),
			("TiltGuardUpZ", EffectiveTiltGuardUpZ),
			("FlankProbeLength", EffectiveFlankProbeLength), ("FlankMargin", EffectiveFlankMargin),
			// v17: effective corridor check values (hotload fallbacks applied), same rule as above.
			("MinCorridorWidth", EffectiveMinCorridorWidth), ("CorridorCheckSkip", EffectiveCorridorCheckSkip),
			("CorridorSampleSpacing", EffectiveCorridorSampleSpacing),
			// v18: effective no-go zone values (hotload fallbacks applied), same rule as above.
			("NoGoZoneRadius", EffectiveNoGoZoneRadius), ("NoGoZoneMinHits", EffectiveNoGoZoneMinHits),
			// v18b: episode gap (effective) for the learn-faster fix.
			("NoGoEpisodeGap", EffectiveNoGoEpisodeGap),
			("StuckAreaRadius", StuckAreaRadius), ("TeleportAfter", TeleportAfter),
			("BaseAggression", BaseAggression), ("AggressionDelay", AggressionDelay), ("AggressionRampTime", AggressionRampTime),
			("AggressionDecayTime", AggressionDecayTime), ("AggressiveCautionScale", AggressiveCautionScale),
			("AggressiveDriveScale", AggressiveDriveScale), ("PushThroughAggression", PushThroughAggression),
			("CornerAimMinAngle", EffectiveCornerAimMinAngleHelper()), ("CornerAimDistance", EffectiveCornerAimDistance),
			("CornerAimCorridor", EffectiveCornerAimCorridor), ("CornerAimTraceRadius", EffectiveCornerAimTraceRadius),
			("CornerAimTraceDistance", EffectiveCornerAimTraceDistance),
			("CornerFullAngle", EffectiveCornerFullAngle), ("SharpCornerSpeed", EffectiveSharpCornerSpeedHelper()),
			("ReverseProgressDistance", EffectiveReverseProgressDistance), ("RepickChainTeleport", EffectiveRepickChainTeleport),
			("HullLength", _hullSize.x), ("HullWidth", _hullSize.y),
		};

		var sb = new StringBuilder();
		for ( int i = 0; i < values.Length; i++ )
		{
			if ( i > 0 ) sb.Append( ", " );
			sb.Append( '"' ).Append( values[i].Item1 ).Append( "\": " ).Append( F( values[i].Item2, 3 ) );
		}
		return sb.ToString();
	}

	/// <summary>
	/// Once per session: random navmesh points, so the analyzer can draw the road network without
	/// access to the scene.
	/// </summary>
	private void SampleNavMeshHelper()
	{
		Vector3? probe = Scene.NavMesh.GetRandomPoint();
		if ( !probe.HasValue ) return; // not loaded yet
		_sessionNavSampled = true;

		var sb = new StringBuilder( "x,y,z\n" );
		for ( int i = 0; i < 4000; i++ )
		{
			Vector3? point = Scene.NavMesh.GetRandomPoint();
			if ( !point.HasValue ) continue;
			sb.Append( F( point.Value.x, 0 ) ).Append( ',' ).Append( F( point.Value.y, 0 ) ).Append( ',' ).Append( F( point.Value.z, 0 ) ).AppendLine();
		}

		AICarTelemetrySink.Put( $"aicar/{_sessionId}/navsample.csv", sb.ToString() );
	}

	// ================================================================== Context probes

	/// <summary>Flat distance from the car to the nearest navmesh point (0 = on the road mesh, 999 = none nearby).</summary>
	private float GetOffNavDistanceHelper( Vector3 position )
	{
		var nav = Scene.NavMesh;
		if ( nav is null || !nav.IsEnabled ) return 0f;
		Vector3? closest = nav.GetClosestPoint( position, 400f );
		return closest.HasValue ? FlatDistance( position, closest.Value ) : 999f;
	}

	/// <summary>Front whisker readings: "w=dist:object" for every whisker that hits something.</summary>
	private string WhiskerSummaryHelper()
	{
		if ( _frontWhiskers is null ) return "";
		string[] labels = { "F", "FL", "FR", "SL", "SR" };
		var sb = new StringBuilder( "whiskers=" );
		bool any = false;
		for ( int i = 0; i < _frontWhiskers.Length; i++ )
		{
			if ( _frontHitDistance[i] >= _frontLength[i] ) continue;
			if ( any ) sb.Append( '|' );
			sb.Append( i < labels.Length ? labels[i] : i.ToString( Inv ) ).Append( ':' )
				.Append( F( _frontHitDistance[i], 0 ) ).Append( ':' ).Append( _frontHitName[i] );
			any = true;
		}
		if ( !any ) sb.Append( "clear" );
		return sb.ToString();
	}

	/// <summary>
	/// 16 flat rays (every 22.5 degrees, car-relative, 0 = ahead, + = left) out to 800 units at
	/// whisker height plus one ray down: "scan=angle:dist:object|..." for hits and "ground=object".
	/// Lets the analyzer see what the car was boxed in by without the scene.
	/// </summary>
	private string ScanSurroundingsHelper()
	{
		const float range = 800f;
		Vector3 origin = WorldTransform.PointToWorld( _hullCenter.WithZ( 0f ) + Vector3.Up * WhiskerHeight );
		var sb = new StringBuilder( "scan=" );
		bool any = false;

		for ( int i = 0; i < 16; i++ )
		{
			float yaw = i * 22.5f;
			Vector3 direction = WorldRotation * Rotation.FromYaw( yaw ).Forward;
			SceneTraceResult r = Scene.Trace.Ray( origin, origin + direction * range )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
			if ( !r.Hit ) continue;

			if ( any ) sb.Append( '|' );
			string name = r.GameObject.IsValid() ? r.GameObject.Name : "?";
			sb.Append( F( yaw > 180f ? yaw - 360f : yaw, 0 ) ).Append( ':' ).Append( F( r.Distance, 0 ) ).Append( ':' ).Append( name );
			any = true;
		}
		if ( !any ) sb.Append( "clear" );

		Vector3 top = WorldPosition + Vector3.Up * 100f;
		SceneTraceResult ground = Scene.Trace.Ray( top, top + Vector3.Down * 400f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		sb.Append( " ground=" ).Append( ground.Hit && ground.GameObject.IsValid() ? ground.GameObject.Name : "none" );
		return sb.ToString();
	}

	// ================================================================== Test cars

	/// <summary>The first AI car in a scene clones itself <see cref="ExtraTestCars"/> times; clones teleport to their own spot.</summary>
	private void SpawnTestCarsHelper()
	{
		if ( ExtraTestCars <= 0 || _clonesSpawnedForScene == Scene ) return;
		_clonesSpawnedForScene = Scene;

		for ( int i = 0; i < ExtraTestCars; i++ )
		{
			// Parked high above so they do not land on this car before their spawn teleport runs.
			GameObject clone = GameObject.Clone( WorldPosition + Vector3.Up * (3000f * (i + 1)) );
			clone.Name = $"{GameObject.Name} (test {i + 1})";
			var driver = clone.Components.Get<AICarDriver>();
			if ( driver.IsValid() )
			{
				driver._teleportPending = true;
				CopyTuningStateToHelper( driver );
			}
		}
	}

	// ================================================================== Helpers

	private bool IsOwnPartHelper( GameObject other )
	{
		GameObject g = other;
		for ( int depth = 0; depth < 32 && g.IsValid(); depth++, g = g.Parent )
		{
			if ( g == GameObject ) return true;
		}
		return false;
	}

	private void TelemetryTeleportHelper( string reason, Vector3 to )
	{
		if ( !_telemetryActive ) return;
		if ( reason != "spawn" )
		{
			TelemetryLegEndHelper( "teleport" );
			CountHelper( "teleports" );
		}
		TelemetryEventHelper( "Teleport", $"reason={reason} to={Fmt( to )} {ScanSurroundingsHelper()}" );
	}

	private void CountHelper( string key )
	{
		if ( _epoch is null ) return;
		_epoch.Counters[key] = (_epoch.Counters.TryGetValue( key, out int v ) ? v : 0) + 1;
	}

	private static float Clamp9999( float value ) => MathF.Min( value, 9999f );

	private static string F( float value, int decimals ) => !float.IsFinite( value ) ? "0" : value.ToString( decimals switch
	{
		0 => "0",
		1 => "0.0",
		2 => "0.00",
		_ => "0.###"
	}, Inv );

	private static string Fmt( Vector3 v ) => $"{F( v.x, 0 )},{F( v.y, 0 )},{F( v.z, 0 )}";

	private static string FmtPath( List<Vector3> path )
	{
		var sb = new StringBuilder();
		for ( int i = 0; i < path.Count; i++ )
		{
			if ( i > 0 ) sb.Append( ';' );
			sb.Append( F( path[i].x, 0 ) ).Append( ',' ).Append( F( path[i].y, 0 ) );
		}
		return sb.ToString();
	}

	private static string Csv( string value ) => (value ?? "").Replace( ',', ' ' ).Replace( '"', '\'' ).Replace( '\n', ' ' ).Replace( '\r', ' ' );

	private static string JsonEscape( string value )
	{
		if ( string.IsNullOrEmpty( value ) ) return "";
		var sb = new StringBuilder( value.Length + 8 );
		foreach ( char c in value )
		{
			switch ( c )
			{
				case '"': sb.Append( "\\\"" ); break;
				case '\\': sb.Append( "\\\\" ); break;
				case '\n': sb.Append( "\\n" ); break;
				case '\r': break;
				case '\t': sb.Append( ' ' ); break;
				default: sb.Append( c ); break;
			}
		}
		return sb.ToString();
	}
}
