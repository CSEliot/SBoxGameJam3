// Project: sboxgamejam3
// File:    ProximityVoiceSpawner.cs
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
// Spawns the per-player ProximityVoice carrier GameObject. Sits on the player prefab
// root; the owning client clones prefabs/voicecarrier.prefab once at start and hands
// the clone its own player root as the follow target.
//
// License:
// This code is provided "as is," without warranty of any kind, express or
// implied, including but not limited to the warranties of merchantability,
// fitness for a particular purpose, and noninfringement. In no event shall
// the authors or copyright holders be liable for any claim, damages, or
// other liability, whether in an action of contract, tort, or otherwise,
// arising from, out of, or in connection with the software or the use or
// other dealings in the software.

namespace Sandbox;

/// <summary>
/// Owner-side spawner for the ProximityVoice carrier (see ProximityVoice.cs for why
/// voice needs a separate object: GameManager disables the player ROOT during the bar
/// menu, the minigame and the end screen, and engine Voice stops recording when
/// disabled - a carrier that lives on its own GameObject keeps voice alive while
/// everyone sits together).
///
/// Only the owning client ever runs the spawn (IsMine() gate): each player's carrier is
/// owned and positioned by that player, so the host must NOT spawn carriers for remote
/// players' body copies - those clients spawn their own, which replicate to everyone.
///
/// IMPORTANT - this component deliberately has NO OnDisabled cleanup. The player root is
/// disabled every time the bar menu / minigame / end screen opens, and destroying or
/// disabling the carrier there would mute the player exactly when they are sitting at
/// the bar talking to friends. The carrier's whole reason to exist is to outlive the
/// player root's disabled state; only OnDestroy (real teardown) touches it.
/// </summary>
[Title( "Proximity Voice Spawner" )]
[Category( "Audio" )]
[Icon( "mic" )]
public sealed class ProximityVoiceSpawner : Component
{
	/// <summary>
	/// Prefab carrying the ProximityVoice component (prefabs/voicecarrier.prefab).
	/// Must be authored on the player prefab; missing => Log.Error, no silent fallback.
	/// </summary>
	[Property] private GameObject _VoiceCarrierPrefab { get; set; }

	/// <summary>
	/// The carrier clone we spawned (owner-local reference; proxies have null because
	/// they never spawn - their copy arrives via network replication instead).
	/// </summary>
	private GameObject _carrier;

	protected override void OnStart()
	{
		// One carrier per player, owned and positioned by that player. Remote players'
		// client clones its own carrier and it replicates to us, so a proxy copy of this
		// component must stay inert (and the host must not spawn one for each remote body).
		if ( !GameObject.Network.IsMine() )
			return;

		// OnStart can't realistically double-fire, but a hotload/restart where this
		// component object was recreated against a still-living carrier shouldn't leave
		// two voice objects per player.
		if ( _carrier.IsValid() )
			return;

		if ( !_VoiceCarrierPrefab.IsValid() )
		{
			Log.Error( "ProximityVoiceSpawner: _VoiceCarrierPrefab is not set on the player prefab - this player gets NO voice. Assign Assets/prefabs/voicecarrier.prefab." );
			return;
		}

		// Clone the prefab root with an identity local transform, then position it at our
		// mouth height so the very first tick before ProximityVoice.OnFixedUpdate takes
		// over is already sane (matches _MouthHeight's default; the carrier re-derives
		// its exact height from its own property afterwards).
		var clone = _VoiceCarrierPrefab.Clone( new Transform( WorldPosition + Vector3.Up * 64f ), null, true, $"VoiceCarrier - {GameObject.Name}" );

		var voice = clone?.GetComponent<ProximityVoice>( true );
		if ( voice is null )
		{
			Log.Error( $"ProximityVoiceSpawner: the voice carrier prefab '{_VoiceCarrierPrefab}' has no ProximityVoice component - destroying the clone, this player gets NO voice." );
			clone?.Destroy();
			return;
		}

		voice.FollowTarget = GameObject;
		_carrier = clone;

		// NetworkSpawn claims ownership for Connection.Local and replicates the carrier
		// (transform included, so proxies get the mouth position interpolated, never
		// simulated). Solo editor play has no session - NetworkSpawn returns false there
		// - so keep the plain local clone, mirroring GameManager.StartGame's solo
		// fallback that clones the player itself without NetworkSpawn. IsMine() stays
		// true on an un-netspawned object, so the carrier still follows and self-gates.
		if ( Networking.IsActive )
			_carrier.NetworkSpawn();
	}

	/// <summary>
	/// Owner teardown: destroy our carrier when the player GameObject is destroyed
	/// (leave/kick). Note we do NOT need the remote side to do anything: the carrier is
	/// network-spawned with NetworkOrphaned=Destroy authored on the prefab, so when the
	/// owner disconnects the engine removes the carrier on every peer anyway - this
	/// OnDestroy just keeps the owner's own view (and solo play) tidy.
	/// </summary>
	protected override void OnDestroy()
	{
		if ( _carrier.IsValid() )
			_carrier.Destroy();
	}
}
