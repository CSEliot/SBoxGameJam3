// Project: sboxgamejam3
// File:    LLMPlaytest.cs
// Author:  cseliot
// Created: 2026.10.10.00.00.00
// Edited:  2026.10.10.00.00.00
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Generic, project-agnostic LLM playtest harness. A GameObjectSystem, so it is
// auto-created for every scene with no scene or prefab wiring; it does nothing
// unless the editor is running a PLAYING game scene AND this scene is the game's
// MAIN scene (never the editor's edit scene, never a UI-owned render scene such as
// the HUD target-bar ScenePanel preview, never a published game). It fakes input
// through the engine's own action bits (no OS-level key injection), polls a script
// file the agent writes, and writes short, grep-friendly telemetry files the agent
// can read back. events.txt and samples.csv are APPEND-ONLY, so a reader only ever
// sees a possibly incomplete final line, never a truncated file.
// 
// This file is IDENTICAL in every project. Project specifics (shortcut handlers
// and extra telemetry fields) live in LLMPlaytest.Project.cs.
// 
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sandbox;

public sealed partial class LLMPlaytest : GameObjectSystem<LLMPlaytest>
{
	// Tag on the local player root; adjust per project when porting.
	public const string PlayerTag = "player";

	private const string DataFolder = "llm-playtest";
	private const string CommandsFile = DataFolder + "/commands.txt";
	private const string CurrentFile = DataFolder + "/current.txt";
	private const string SummaryFile = "summary.txt";
	private const string EventsFile = "events.txt";
	private const string SamplesFile = "samples.csv";
	private const float PollInterval = 0.25f;
	private const float FlushInterval = 1.0f;
	private const int MaxSummaryLines = 25;
	private const int SummaryEventTail = 8;
	private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

	// Session state (one session per play session, main scene only).
	private bool _sessionActive;
	private string _sessionId, _sessionFolder;
	private float _sessionStartTime;

	// Command polling and the running script.
	private float _pollAccum, _flushAccum;
	private HashSet<string> _knownActions;
	private string _lastScriptId = "", _runningScriptId, _pendingDone, _pendingRelease;
	private List<Cmd> _script;
	private int _cmdIndex;
	private float _cmdWait;

	// Held actions: *Fresh marks the first application in a context as a fresh press; _pressFixed
	// queues a frame-context press for the fixed context.
	private readonly HashSet<string> _held = new(), _heldFreshFrame = new(), _heldFreshFixed = new(), _pressFixed = new();

	// Lines queued since the last flush (<= one flush interval; the files are append-only).
	private readonly List<string> _pendingSamples = new(), _pendingEvents = new();
	private int _sampleCount, _eventCount, _errorEventCount;
	private string _sampleHeader;
	private bool _sampleHeaderWritten;
	private List<string> _projectColumns;

	// Summary head built by the sample, consumed by the flush.
	private List<string> _summaryHead;
	private string _summaryText = "", _dataPath = "(unavailable)";
	private GameObject _localPlayer;

	public LLMPlaytest( Scene scene ) : base( scene )
	{
		// Negative order so injection runs before the default (order 0) hooks and component reads.
		Listen( Stage.StartUpdate, -100, OnFrameHelper, "LLMPlaytest frame" );
		Listen( Stage.StartFixedUpdate, -100, OnFixedFrameHelper, "LLMPlaytest fixed" );
	}

	public override void Dispose()
	{
		if ( _sessionActive )
		{
			EmitEventHelper( "session", "end" );
			FlushHelper();
		}
		_sessionActive = false;
		base.Dispose();
	}

	private void OnFrameHelper()
	{
		if ( !EnsureSessionHelper() ) return;
		_pollAccum += Time.Delta;
		if ( _pollAccum >= PollInterval )
		{
			_pollAccum = 0f;
			PollCommandsHelper();
		}
		StepScriptHelper();
		ApplyHeldHelper( false );
		_flushAccum += Time.Delta;
		if ( _flushAccum >= FlushInterval )
		{
			_flushAccum = 0f;
			SampleHelper();
			FlushHelper();
		}
		ProjectFrameHelper();
	}

	// Fixed-step hook: applies held actions and the queued one-shot presses in the fixed context.
	private void OnFixedFrameHelper()
	{
		if ( !_sessionActive ) return;
		ApplyHeldHelper( true );
		foreach ( var action in _pressFixed ) Input.SetAction( action, true );
		_pressFixed.Clear();
	}

	// Idle unless the editor runs a playing game whose MAIN scene this is.
	private bool EnsureSessionHelper()
	{
		// Never the editor's edit scene, never a standalone/published game.
		if ( !Game.IsEditor || Scene.IsEditor ) return false;
		// Main scene only: a ScenePanel render scene is also Game.ActiveScene but has WantsSystemScene=false (ScenePanel.cs:71).
		if ( !Scene.WantsSystemScene || Scene != Game.ActiveScene ) return false;
		if ( !_sessionActive )
			StartSessionHelper();
		return _sessionActive;
	}

	// Apply each held action; the first application in a context is a press, later ones set the last-action bit.
	private void ApplyHeldHelper( bool fixedContext )
	{
		var fresh = fixedContext ? _heldFreshFixed : _heldFreshFrame;
		foreach ( var action in _held )
		{
			Input.SetAction( action, true );
			if ( !fresh.Remove( action ) ) Input.SetLastAction( action, true );
		}
	}

	// Block the running script until a project poller clears it; doneRaw is the completion event.
	private void BlockScriptHelper( string doneRaw )
	{
		_pendingDone = doneRaw;
		_cmdWait = float.PositiveInfinity;
	}

	// First locally-owned origin object tagged PlayerTag (un-networked counts as local); cached.
	private GameObject GetLocalPlayerRootHelper()
	{
		if ( _localPlayer.IsValid() && IsLocalHelper( _localPlayer ) ) return _localPlayer;
		_localPlayer = Scene.FindAllWithTag( PlayerTag ).Where( x => x.Parent is null || !x.Parent.Tags.Has( PlayerTag ) ).FirstOrDefault( IsLocalHelper );
		return _localPlayer;
	}

	private static bool IsLocalHelper( GameObject obj ) =>
		obj.IsValid() && (!obj.Network.Active || obj.Network.IsOwner);

	private void StartSessionHelper()
	{
		_sessionId = DateTime.Now.ToString( "yyyyMMdd-HHmmss", Culture );
		_sessionFolder = DataFolder + "/" + _sessionId;
		_sessionStartTime = Time.Now;
		_sessionActive = true;

		// Reset per-session state (a scene reload restarts the session in a fresh folder).
		_pendingSamples.Clear();
		_pendingEvents.Clear();
		_sampleCount = 0;
		_eventCount = 0;
		_sampleHeader = null;
		_sampleHeaderWritten = false;
		_projectColumns = null;
		_summaryHead = new List<string> { $"session: {_sessionId}", "t: 0.0", $"scene: {Scene.Name}",
			"script: none", "pos: -,-,-", "speed: -", "yaw: -" };

		try
		{
			FileSystem.Data.CreateDirectory( _sessionFolder );
			// A commands.txt present at play start is archived unread, never replayed.
			if ( FileSystem.Data.FileExists( CommandsFile ) )
			{
				var stale = FileSystem.Data.ReadAllText( CommandsFile );
				if ( stale != null ) FileSystem.Data.WriteAllText( _sessionFolder + "/stale-commands.txt", stale );
				FileSystem.Data.DeleteFile( CommandsFile );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"LLMPlaytest: session setup failed: {e.Message}" );
		}

		_dataPath = FileSystem.Data.GetFullPath( DataFolder ) ?? "(unavailable)";
		Log.Info( $"LLMPlaytest: data={_dataPath}" );
		WriteFileHelper( CurrentFile, _sessionId + "\n" );
		EmitEventHelper( "session", $"start scene={Scene.Name} data={_dataPath}" );
		FlushHelper();
	}

	// Whole-file write, used only for the tiny current.txt/summary.txt (no rename: System.IO is not whitelisted).
	private void WriteFileHelper( string relativePath, string contents )
	{
		try
		{
			FileSystem.Data.WriteAllText( relativePath, contents );
		}
		catch ( Exception e )
		{
			Log.Warning( $"LLMPlaytest: write '{relativePath}' failed: {e.Message}" );
		}
	}

	// Append-only: OpenWrite(Append) extends in place; write bytes via Stream, never StreamWriter (not whitelisted, BaseAccess.cs:39).
	private void AppendFileHelper( string relativePath, string contents )
	{
		if ( string.IsNullOrEmpty( contents ) ) return;
		try
		{
			using var stream = FileSystem.Data.OpenWrite( relativePath, System.IO.FileMode.Append );
			var bytes = Encoding.UTF8.GetBytes( contents );
			stream.Write( bytes, 0, bytes.Length );
		}
		catch ( Exception e )
		{
			Log.Warning( $"LLMPlaytest: append '{relativePath}' failed: {e.Message}" );
		}
	}

	private void FlushHelper()
	{
		if ( !_sessionActive ) return;

		// samples.csv: the header once, then only the rows produced since the last flush.
		if ( _pendingSamples.Count > 0 && _sampleHeader != null )
		{
			var rows = new StringBuilder();
			if ( !_sampleHeaderWritten )
			{
				rows.Append( _sampleHeader ).Append( '\n' );
				_sampleHeaderWritten = true;
			}
			foreach ( var row in _pendingSamples ) rows.Append( row ).Append( '\n' );
			AppendFileHelper( _sessionFolder + "/" + SamplesFile, rows.ToString() );
			_pendingSamples.Clear();
		}

		// events.txt: only the lines produced since the last flush.
		if ( _pendingEvents.Count > 0 )
		{
			var events = new StringBuilder();
			foreach ( var line in _pendingEvents ) events.Append( line ).Append( '\n' );
			AppendFileHelper( _sessionFolder + "/" + EventsFile, events.ToString() );
			_pendingEvents.Clear();
		}

		// summary.txt is tiny: a whole-file rewrite keeps it current.
		BuildSummaryHelper();
		WriteFileHelper( _sessionFolder + "/" + SummaryFile, _summaryText );
	}

	private void EmitEventHelper( string kind, string payload )
	{
		var t = _sessionActive ? (Time.Now - _sessionStartTime) : 0f;
		var head = $"t={t.ToString( "0.0", Culture )} {kind}";
		_pendingEvents.Add( string.IsNullOrEmpty( payload ) ? head : head + " " + payload );
		_eventCount++;
		if ( kind == "error" ) _errorEventCount++;
	}

	private readonly record struct Cmd( string Name, string[] Args, string Raw );

	private void PollCommandsHelper()
	{
		// Only accept a new script while idle: one already running keeps the floor.
		if ( _script != null || !FileSystem.Data.FileExists( CommandsFile ) ) return;

		string text;
		try
		{
			text = FileSystem.Data.ReadAllText( CommandsFile );
		}
		catch ( Exception e )
		{
			Log.Warning( $"LLMPlaytest: read commands.txt failed: {e.Message}" );
			return;
		}

		var lines = ParseScriptHelper( text );
		// The first line must be the `run <id>` header; anything else is a half-written file.
		if ( lines.Count == 0 || lines[0].Name != "run" || lines[0].Args.Length < 1 ) return;
		var id = lines[0].Args[0];
		if ( id == _lastScriptId ) return;

		_lastScriptId = id;
		_runningScriptId = id;
		_script = lines.GetRange( 1, lines.Count - 1 );
		_cmdIndex = 0;
		_cmdWait = 0f;
		_pendingDone = null;
		_pendingRelease = null;
		_held.Clear();
		_heldFreshFrame.Clear();
		_heldFreshFixed.Clear();
		_pressFixed.Clear();
		EmitEventHelper( "script", $"id={id} start" );
	}

	private static List<Cmd> ParseScriptHelper( string text )
	{
		var list = new List<Cmd>();
		if ( string.IsNullOrEmpty( text ) ) return list;
		foreach ( var rawLine in text.Replace( "\r", "" ).Split( '\n' ) )
		{
			var line = rawLine.Trim();
			if ( line.Length == 0 || line.StartsWith( "#" ) ) continue;
			var parts = line.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			if ( parts.Length > 0 ) list.Add( new Cmd( parts[0].ToLowerInvariant(), parts.Skip( 1 ).ToArray(), line ) );
		}
		return list;
	}

	private void StepScriptHelper()
	{
		if ( _script == null ) return;

		if ( _cmdWait > 0f )
		{
			_cmdWait -= Time.Delta;
			if ( _cmdWait > 0f ) return;
		}

		// A blocking command finished (or a project poller cut it short).
		if ( _pendingDone != null )
		{
			if ( _pendingRelease != null )
			{
				RemoveHeldHelper( _pendingRelease );
				_pendingRelease = null;
			}
			EmitEventHelper( "done", _pendingDone );
			_pendingDone = null;
		}

		while ( _script != null && _cmdWait <= 0f )
		{
			if ( _cmdIndex >= _script.Count )
			{
				EmitEventHelper( "script", $"id={_runningScriptId} done" );
				_script = null;
				_runningScriptId = null;
				break;
			}
			RunCommandHelper( _script[_cmdIndex++] );
			if ( _cmdWait > 0f ) break;
		}
	}

	private void RunCommandHelper( Cmd cmd )
	{
		EmitEventHelper( "cmd", cmd.Raw );
		try
		{
			switch ( cmd.Name )
			{
				case "down":
				case "up":
				case "press":
					if ( RequireActionHelper( cmd, 0 ) )
					{
						var action = cmd.Args[0];
						if ( cmd.Name == "down" ) AddHeldHelper( action );
						else if ( cmd.Name == "up" ) RemoveHeldHelper( action );
						// press: frame readers see it now; fixed readers on the next step that runs.
						else
						{
							Input.SetAction( action, true );
							_pressFixed.Add( action );
						}
						EmitEventHelper( "done", cmd.Raw );
					}
					break;

				case "hold":
					if ( !RequireActionHelper( cmd, 0 ) || !TrySecondsHelper( cmd, 1, out var holdSeconds ) ) break;
					if ( holdSeconds <= 0f )
					{
						EmitEventHelper( "done", cmd.Raw );
						break;
					}
					AddHeldHelper( cmd.Args[0] );
					_pendingRelease = cmd.Args[0];
					_cmdWait = holdSeconds;
					_pendingDone = cmd.Raw;
					break;

				case "wait":
					if ( !TrySecondsHelper( cmd, 0, out var waitSeconds ) ) break;
					if ( waitSeconds <= 0f )
					{
						EmitEventHelper( "done", cmd.Raw );
						break;
					}
					_cmdWait = waitSeconds;
					_pendingDone = cmd.Raw;
					break;

				case "shortcut":
					if ( cmd.Args.Length < 1 )
					{
						EmitEventHelper( "error", $"{cmd.Raw} reason=missing-name" );
						break;
					}
					bool handled = false;
					var errorsBefore = _errorEventCount;
					ProjectShortcutHelper( cmd.Args[0], cmd.Args.Skip( 1 ).ToArray(), ref handled );
					// The hook owns the outcome: a blocking shortcut sets a pending completion, a
					// failing one emits its own error. Emit the generic done only when the hook did
					// neither (a plain success and no error).
					if ( !handled ) EmitEventHelper( "error", $"{cmd.Raw} reason=unknown-shortcut" );
					else if ( _pendingDone == null && _errorEventCount == errorsBefore ) EmitEventHelper( "done", cmd.Raw );
					break;

				case "mark":
					EmitEventHelper( "mark", "text=" + string.Join( ' ', cmd.Args ) );
					EmitEventHelper( "done", cmd.Raw );
					break;

				default:
					EmitEventHelper( "error", $"{cmd.Raw} reason=unknown-command" );
					break;
			}
		}
		catch ( Exception e )
		{
			EmitEventHelper( "error", $"{cmd.Raw} reason={e.Message}" );
		}
	}

	// Start holding: mark it fresh in both contexts so its first application each is a press.
	private void AddHeldHelper( string action )
	{
		_held.Add( action );
		_heldFreshFrame.Add( action );
		_heldFreshFixed.Add( action );
	}

	private void RemoveHeldHelper( string action )
	{
		_held.Remove( action );
		_heldFreshFrame.Remove( action );
		_heldFreshFixed.Remove( action );
	}

	private bool RequireActionHelper( Cmd cmd, int argIndex )
	{
		if ( cmd.Args.Length <= argIndex )
		{
			EmitEventHelper( "error", $"{cmd.Raw} reason=missing-action" );
			return false;
		}
		// Also guards against SetAction's -1 index, which would flip an unrelated bit.
		_knownActions ??= new HashSet<string>( Input.ActionNames, StringComparer.OrdinalIgnoreCase );
		if ( _knownActions.Contains( cmd.Args[argIndex] ) ) return true;
		EmitEventHelper( "error", $"{cmd.Raw} reason=unknown-action" );
		return false;
	}

	private bool TrySecondsHelper( Cmd cmd, int argIndex, out float seconds )
	{
		seconds = 0f;
		if ( cmd.Args.Length <= argIndex )
		{
			EmitEventHelper( "error", $"{cmd.Raw} reason=missing-seconds" );
			return false;
		}
		if ( float.TryParse( cmd.Args[argIndex], NumberStyles.Float, Culture, out seconds ) && seconds >= 0f ) return true;
		EmitEventHelper( "error", $"{cmd.Raw} reason=bad-seconds" );
		return false;
	}

	private void SampleHelper()
	{
		var t = Time.Now - _sessionStartTime;
		var player = GetLocalPlayerRootHelper();
		var px = "-";
		var py = "-";
		var pz = "-";
		var speed = "-";
		var yaw = "-";

		if ( player.IsValid() )
		{
			var pos = player.WorldPosition;
			px = pos.x.ToString( "0.0", Culture );
			py = pos.y.ToString( "0.0", Culture );
			pz = pos.z.ToString( "0.0", Culture );
			yaw = player.WorldRotation.Angles().yaw.ToString( "0.0", Culture );
			var body = player.GetComponent<Rigidbody>( true );
			if ( body.IsValid() ) speed = body.Velocity.Length.ToString( "0.0", Culture );
		}

		var fields = new List<(string Name, string Value)>();
		ProjectSampleHelper( fields );

		if ( _sampleHeader == null )
		{
			_projectColumns = fields.ConvertAll( f => CsvHelper( f.Name ) );
			_sampleHeader = "t,px,py,pz,speed,yaw" + string.Concat( _projectColumns.Select( c => "," + c ) );
		}

		var row = new StringBuilder();
		row.Append( t.ToString( "0.0", Culture ) ).Append( ',' ).Append( px ).Append( ',' ).Append( py ).Append( ',' )
			.Append( pz ).Append( ',' ).Append( speed ).Append( ',' ).Append( yaw );
		for ( int i = 0; i < _projectColumns.Count; i++ )
			row.Append( ',' ).Append( i < fields.Count ? CsvHelper( fields[i].Value ) : "" );

		_pendingSamples.Add( row.ToString() );
		_sampleCount++;
		ProjectEventsHelper();

		// Head of summary.txt; the flush appends the counts and the event tail.
		_summaryHead = new List<string> { $"session: {_sessionId}", $"t: {t.ToString( "0.0", Culture )}",
			$"scene: {Scene.Name}", $"script: {ScriptLineHelper()}", $"pos: {px},{py},{pz}", $"speed: {speed}", $"yaw: {yaw}" };
		foreach ( var f in fields ) _summaryHead.Add( $"{f.Name}: {f.Value}" );
	}

	private string ScriptLineHelper() => _runningScriptId != null
		? _runningScriptId + " running"
		: (string.IsNullOrEmpty( _lastScriptId ) ? "none" : _lastScriptId + " done");

	private void BuildSummaryHelper()
	{
		var lines = new List<string>( _summaryHead ) {
			$"events: {_eventCount}", $"samples: {_sampleCount}", "-- last events --"
		};
		lines.AddRange( ReadEventTailHelper( Math.Min( SummaryEventTail, Math.Max( 0, MaxSummaryLines - lines.Count ) ) ) );
		while ( lines.Count > MaxSummaryLines ) lines.RemoveAt( lines.Count - 1 );
		_summaryText = string.Join( '\n', lines ) + "\n";
	}

	// Last non-empty lines of events.txt, read back through FileSystem.Data; empty on any failure.
	private List<string> ReadEventTailHelper( int count )
	{
		var tail = new List<string>();
		if ( count <= 0 ) return tail;
		try
		{
			var text = FileSystem.Data.ReadAllText( _sessionFolder + "/" + EventsFile );
			if ( string.IsNullOrEmpty( text ) ) return tail;
			var lines = text.Replace( "\r", "" ).Split( '\n' );
			var end = lines.Length;
			if ( end > 0 && lines[end - 1].Length == 0 ) end--; // the final newline leaves an empty element
			for ( int i = Math.Max( 0, end - count ); i < end; i++ )
				if ( lines[i].Length > 0 ) tail.Add( lines[i] );
		}
		catch ( Exception e )
		{
			Log.Warning( $"LLMPlaytest: read events tail failed: {e.Message}" );
		}
		return tail;
	}

	private static string CsvHelper( string value ) =>
		string.IsNullOrEmpty( value ) ? "" : value.Replace( ',', '|' ).Replace( '\n', ' ' ).Replace( '\r', ' ' );

	// Project shortcut hook: set handled true if the shortcut ran; a blocking shortcut calls BlockScriptHelper.
	partial void ProjectShortcutHelper( string name, string[] args, ref bool handled );

	// Project poller, called every frame after the script step (unblocks shortcuts).
	partial void ProjectFrameHelper();

	// Project telemetry fields; emit the SAME names every call so the CSV header stays fixed.
	partial void ProjectSampleHelper( List<(string Name, string Value)> fields );

	// Project change-detection events, called once per sample.
	partial void ProjectEventsHelper();
}
