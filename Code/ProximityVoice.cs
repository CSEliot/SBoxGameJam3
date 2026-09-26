// Project: sboxgamejam3
// File:    ProximityVoice.cs
// Author:  cseliot
// Created: 2026.09.26
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
//
// Description:
// Distance-gated voice chat. Lives on a per-player, owner-spawned carrier GameObject
// (see ProximityVoiceSpawner), NOT on the player root: GameManager disables the player
// root during the bar menu, the minigame and the end screen, and the engine Voice
// unsubscribes and stops recording when disabled - a Voice on the player root would
// mute you exactly when players sit together at a bar. The carrier stays enabled,
// follows the body's position, and replicates its transform so every client can gate
// hearing (receiver side) and sending (sender side) by mouth-to-mouth distance.
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
using System.Linq;

namespace Sandbox;

/// <summary>
/// Proximity voice chat, sender- and receiver-gated by distance between carrier
/// "mouths". Runs on the owner-spawned carrier object so it is never caught by the
/// player root being disabled (bar menu / minigame / end screen mutes a player-root
/// Voice because Voice.OnDisabledInternal stops recording).
///
/// Two gates cooperate:
/// 1. ExcludeFilter() (sender): drops far-away listeners from the RPC fan-out so dead
///    bandwidth isn't spent on voices nobody would play. Prune only - not authoritative.
/// 2. ShouldHearVoice() (receiver): the authoritative gate, with hysteresis
///    (_EnterRadius to start hearing, _ExitRadius to keep hearing) so two mouths
///    hovering at the boundary don't crackle in and out of audibility.
///
/// The carrier's transform is network-synced and interpolated, and the receiver always
/// measures against its own (slightly stale) copy of the speaker's mouth - hence the
/// _SendMargin head-room on the sender side.
/// </summary>
[Title( "Proximity Voice" )]
[Category( "Audio" )]
[Icon( "record_voice_over" )]
public sealed class ProximityVoice : Voice
{
	/// <summary>
	/// Distance (engine units) at or inside which a receiver starts hearing a speaker's
	/// voice. 0 or negative disables proximity gating entirely - global voice - following
	/// the project's "&lt;= 0 disables" kill-switch convention (e.g. DrunkCC._MaxHitRoll);
	/// "hear nobody" is deliberately NOT what 0 means.
	/// </summary>
	[Property] private float _EnterRadius { get; set; } = 2000f;

	/// <summary>
	/// Beyond this distance a receiver stops hearing a speaker it was ALREADY hearing.
	/// The band between _EnterRadius and _ExitRadius is hysteresis: replicated mouth
	/// positions jitter a little every tick, and without an exit radius wider than the
	/// enter radius, a pair of mouths parked near the boundary would crackle in and out.
	/// Treated as _EnterRadius if smaller (clamped, so a mistuned author value can't
	/// invert the gate).
	/// </summary>
	[Property] private float _ExitRadius { get; set; } = 2400f;

	/// <summary>
	/// Extra distance the sender keeps transmitting beyond _ExitRadius. The receiver's
	/// gate measures against its own interpolated, slightly stale copy of the speaker's
	/// mouth, so the sender's prune cutoff is padded by this margin - a listener just
	/// inside the gate is never pruned into silence by position latency.
	/// </summary>
	[Property] private float _SendMargin { get; set; } = 200f;

	/// <summary>
	/// The carrier rides this far above the followed body's origin. The player root's
	/// origin is at the feet, so this puts the measurement/playback point near head
	/// height; WorldspacePlayback then pans the voice from the mouth, not the floor.
	/// </summary>
	[Property] private float _MouthHeight { get; set; } = 64f;

	/// <summary>
	/// The body this carrier shadows (the player root, set by ProximityVoiceSpawner on
	/// the owning client right after Clone). Deliberately NOT a [Property]: it is a
	/// live scene reference only the owner has; proxies never read it because their
	/// transform replicates from the owner instead.
	/// </summary>
	public GameObject FollowTarget { get; set; }

	/// <summary>
	/// The carrier THIS client owns, cached so the per-packet receiver path never has to
	/// scan the scene. Set in OnStart on the owner's copy, cleared in OnDestroy.
	/// </summary>
	private static ProximityVoice _localCarrier;

	/// <summary>
	/// Reused scratch list for the Scene.GetAll&lt;ProximityVoice&gt; scans. Static
	/// because both the sender prune (ExcludeFilter, up to 30 Hz while talking) and the
	/// receiver re-scan (ResolveLocalCarrierHelper) run single-threaded on the game
	/// thread and must not allocate per call.
	/// </summary>
	private static readonly List<ProximityVoice> _carrierScratch = new();

	/// <summary>
	/// Reused exclusion list returned by ExcludeFilter (the engine only reads it inside
	/// the Rpc.FilterExclude using-scope, so returning one cleared field is safe).
	/// Transient scratch, rebuilt from Connection.All on every call and cleared first -
	/// it carries NO state between packets, so the host-migration warning does not
	/// apply: a migrated-to host just rebuilds it from its own fresh Connection.All.
	/// </summary>
#pragma warning disable SB3002 // scratch List<Connection> is per-call, not persisted state
	private readonly List<Connection> _excluded = new();
#pragma warning restore SB3002

	/// <summary>
	/// True once FollowTarget has been assigned. Only then does losing the target mean
	/// "the player was destroyed" (as opposed to "the spawner hasn't set it yet").
	/// </summary>
	private bool _hadFollowTarget;

	/// <summary>
	/// Hysteresis state: are we currently inside this speaker's exit band?
	/// </summary>
	private bool _isHeard;

	/// <summary>
	/// Time since the last voice packet from this speaker passed the gate check. If it
	/// exceeds a second the speaker has gone quiet and the next utterance must re-enter
	/// through _EnterRadius rather than coast on the wider _ExitRadius.
	/// </summary>
	private RealTimeSince _sinceGateCheck;

	/// <summary>
	/// Throttle for the lazy lip-sync renderer probe (once per second, never per packet).
	/// </summary>
	private RealTimeSince _sinceRendererProbe;

	protected override void OnStart()
	{
		// Cache our own carrier up front so the first voice packet doesn't pay for a scan.
		if ( GameObject.Network.IsMine() )
			_localCarrier = this;
	}

	protected override void OnDestroy()
	{
		if ( ReferenceEquals( _localCarrier, this ) )
			_localCarrier = null;
	}

	/// <summary>
	/// Owner side: shadow the followed body at mouth height. Runs every fixed tick
	/// whether or not the player root is ENABLED - a disabled GameObject still has a
	/// valid transform, which is exactly the seated-at-the-bar case GameManager disables
	/// the root for. Proxies do nothing: their transform replicates from the owner and
	/// is interpolated by the network system.
	///
	/// Every client (owner and proxies alike) also gets the throttled lip-sync renderer
	/// probe here, since Voice drives viseme morphs off Renderer on all copies.
	/// </summary>
	protected override void OnFixedUpdate()
	{
		if ( GameObject.Network.IsMine() )
		{
			if ( FollowTarget.IsValid() )
			{
				_hadFollowTarget = true;
				WorldPosition = FollowTarget.WorldPosition + Vector3.Up * _MouthHeight;
			}
			else if ( _hadFollowTarget )
			{
				// A target was assigned and is now gone: our player was destroyed (left /
				// kicked). An orphan carrier would broadcast a stale mouth position
				// forever, so take it down with the player. On a networked carrier the
				// destroy replicates (NetworkOrphaned handles peers); on a solo clone the
				// GameObject simply goes away.
				GameObject.Destroy();
				return;
			}
		}

		// Engine Voice only enables lip sync when the voice sound is first created
		// (VoiceComponent.cs:352), so a Renderer resolved mid-utterance takes effect
		// from the NEXT utterance - cheap, but not instant. Throttle to 1 Hz: this only
		// needs to catch spawn orderings and re-dress churn, not every tick.
		if ( LipSync && !Renderer.IsValid() && _sinceRendererProbe > 1f )
			ResolveLipSyncRendererHelper();
	}

	/// <summary>
	/// Find the SkinnedModelRenderer the engine should drive viseme morphs on: the
	/// followed body's Dresser BodyTarget (the body renderer under the clothing root).
	/// Owner uses FollowTarget directly; proxies (who never have a FollowTarget - it is
	/// owner-local) locate the matching player root by owner id. Only runs at 1 Hz from
	/// the probe in OnFixedUpdate, never per voice packet.
	/// </summary>
	private void ResolveLipSyncRendererHelper()
	{
		_sinceRendererProbe = 0;

		GameObject playerRoot;

		if ( GameObject.Network.IsMine() )
		{
			playerRoot = FollowTarget.IsValid() ? FollowTarget : null;
		}
		else
		{
			// The carrier's OwnerId is populated on proxies before OnStart, so the
			// speaker's player root is findable by matching owner ids.
			playerRoot = Scene.FindAllWithTagOrigin( "player" )
				.FirstOrDefault( p => p.Network.OwnerId == GameObject.Network.OwnerId );
		}

		if ( playerRoot is null )
			return;

		// includeDisabled: the player root (and so the Dresser) is disabled while the
		// bar menu / minigame / end screen is up - the carrier must still find the mouth.
		Renderer = playerRoot.GetComponentInChildren<Dresser>( true )?.BodyTarget;
	}

	/// <summary>
	/// Return the carrier this client owns, from the static cache when it is still valid,
	/// else re-scan the scene's ProximityVoice type index (no hierarchy walk) and cache.
	/// Null when this client has no carrier yet (pre-spawn packets) - callers fail CLOSED.
	/// </summary>
	private ProximityVoice ResolveLocalCarrierHelper()
	{
		if ( _localCarrier.IsValid() )
			return _localCarrier;

		_carrierScratch.Clear();
		Scene.GetAll( _carrierScratch );

		for ( int i = 0; i < _carrierScratch.Count; i++ )
		{
			if ( _carrierScratch[i].GameObject.Network.IsMine() )
			{
				_localCarrier = _carrierScratch[i];
				return _localCarrier;
			}
		}

		return null;
	}

	/// <summary>
	/// Receiver gate - the authoritative proximity decision. Runs INSIDE the replicated
	/// Msg_Voice on OUR copy of the SPEAKER's carrier ('this' is the speaker), with
	/// Rpc.Caller being the speaker's connection. Note the broadcast also runs locally on
	/// the sender with Caller == Connection.Local, and the engine then sets sound.Loopback,
	/// so our OWN carrier must answer true here or Voice.Loopback can never be heard.
	///
	/// Distance is measured mouth-to-mouth with hysteresis: inside _EnterRadius (or,
	/// already hearing, inside _ExitRadius) => true. A speaker who has been silent for a
	/// second re-enters through the tighter _EnterRadius.
	/// </summary>
	protected override bool ShouldHearVoice( Connection connection )
	{
		// Kill switch (see _EnterRadius): proximity disabled => global voice.
		if ( _EnterRadius <= 0f )
			return true;

		// Our own voice / loopback path: the engine's Loopback flag, not distance,
		// decides whether we actually hear ourselves.
		if ( GameObject.Network.IsMine() )
			return true;

		// Defensive: the caller must own the carrier whose packet we are hearing.
		if ( connection is null || connection.Id != GameObject.Network.OwnerId )
		{
			_isHeard = false;
			return false;
		}

		ProximityVoice listener = ResolveLocalCarrierHelper();
		if ( listener is null )
		{
			// Fail CLOSED: no local carrier resolved (ours not spawned yet) means no
			// valid listener mouth - don't play audio we can't distance-gate.
			_isHeard = false;
			return false;
		}

		// Quiet for a second => the utterance ended => the next one must re-enter at
		// _EnterRadius instead of coasting on the wider exit band.
		if ( _sinceGateCheck > 1f )
			_isHeard = false;

		float radius = _isHeard ? MathF.Max( _ExitRadius, _EnterRadius ) : _EnterRadius;
		float distanceSquared = (WorldPosition - listener.WorldPosition).LengthSquared;

		_isHeard = distanceSquared <= radius * radius;
		_sinceGateCheck = 0;

		return _isHeard;
	}

	/// <summary>
	/// Sender prune - runs on the SENDER's own carrier, up to ~30 Hz while talking.
	/// Returns the connections that should NOT receive this voice packet: everyone whose
	/// carrier mouth is farther than _ExitRadius + _SendMargin (the margin absorbs
	/// position latency in the receiver's stale copy), plus connections whose carrier is
	/// not replicated here yet (we cannot measure them - excluding them is the cheap
	/// guess, and it self-heals as soon as their carrier arrives).
	///
	/// This is bandwidth pruning ONLY; ShouldHearVoice on the receiver is the
	/// authoritative gate. Any mismatch in Connection instances or carrier freshness
	/// therefore degrades to extra packets that the receiver simply drops - never to
	/// voice being wrongly gated off.
	///
	/// Allocation-light: one reused exclusion list returned, one static scratch list
	/// filled by the Scene.GetAll type index (no hierarchy walk, no LINQ here). The one
	/// per-call allocation is the Connection.All getter itself, which builds a new list on
	/// every access; the engine's own client-side Broadcast pays the same cost per packet.
	/// </summary>
	protected override IEnumerable<Connection> ExcludeFilter()
	{
		_excluded.Clear();

		// Kill switch (see _EnterRadius): global voice => exclude nobody.
		if ( _EnterRadius <= 0f )
			return _excluded;

		float cutoff = MathF.Max( _ExitRadius, _EnterRadius ) + _SendMargin;
		float cutoffSquared = cutoff * cutoff;

		_carrierScratch.Clear();
		Scene.GetAll( _carrierScratch );

		foreach ( var connection in Connection.All )
		{
			// We never "send" to ourselves; the local loopback path doesn't go through
			// the wire.
			if ( connection == Connection.Local )
				continue;

			ProximityVoice carrier = null;
			for ( int i = 0; i < _carrierScratch.Count; i++ )
			{
				if ( _carrierScratch[i].GameObject.Network.OwnerId == connection.Id )
				{
					carrier = _carrierScratch[i];
					break;
				}
			}

			if ( carrier is null )
			{
				_excluded.Add( connection );
				continue;
			}

			float distanceSquared = (WorldPosition - carrier.WorldPosition).LengthSquared;
			if ( distanceSquared > cutoffSquared )
				_excluded.Add( connection );
		}

		return _excluded;
	}
}
