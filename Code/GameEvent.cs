// Project: sboxgamejam3
// File:    GameEvent.cs
// Author:  cseliot
// Created: 2026.09.27.03.30.42
// Edited: 2026.09.27.03.30.42
// 
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
// 
// This source code is the property of KiteLion Games, LLC and may not be
// copied, distributed, modified, or used in any way without prior written
// permission from KiteLion Games, LLC.
// 
// Description:
// Abstract base for "bar-exit events": scene components GameManager discovers at startup
// and rolls for exactly once each time the local player exits a bar after finishing the
// drinking mini-game. Editor-set per event: the beer level that unlocks it and the %
// chance it fires.
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
/// Base class for bar-exit events. By design these fire ONLY when the local player exits
/// a bar after a mini-game round - GameManager.RollBarExitEventHelper is the sole trigger
/// path; do not add others. GameManager queries every ENABLED GameEvent component in the
/// scene live at roll time. An event is an eligible candidate on bar exit when:
/// 1. the LOCAL player's DrunkCC.BeerLevel >= MinBeerLevel (unlock gate),
/// 2. IsStarted is still false (one-shot latch - events have no Stop, see StartEvent),
/// 3. TriggerChancePercent > 0, after which GameManager rolls its per-client RNG once.
/// All of this is LOCAL state: BeerLevel is not network-synced and event payloads such as
/// post-process effects ride the per-client camera, so each client rolls and experiences
/// its own events independently.
/// Concrete events live on a scene GameObject the user authors later; that node is
/// authored ENABLED (so the startup scan finds it) while the event's visual payload
/// (e.g. ChromaticPostProcess) is authored disabled - enabling it IS "starting" the event,
/// mirroring the bar Enter-Ring pattern.
/// </summary>
public abstract class GameEvent : Component
{
	/// <summary>
	/// Local beer level (DrunkCC.BeerLevel) required before this event is an eligible
	/// candidate when exiting a bar. Editor-set; neutral default 0 (always unlocked -
	/// authoring intent like "20 beers" belongs in the scene, not in code defaults).
	/// </summary>
	[Property] public int MinBeerLevel { get; set; } = 0;

	/// <summary>
	/// Percent chance (0-100) this event starts after exiting a bar, once unlocked by
	/// MinBeerLevel. Editor-set; neutral default 0 (fires nothing until wired up in the
	/// inspector). Range slider per the engine's own usage (ChromaticAberration.cs uses
	/// [Property, Range( 0, 1 )]; RangeAttribute lives in Sandbox.System/Attributes/Range.cs).
	/// </summary>
	[Property, Range( 0, 100 )] public float TriggerChancePercent { get; set; } = 0f;

	/// <summary>
	/// One-shot latch maintained by GameManager.RollBarExitEventHelper (set on
	/// StartEvent SUCCESS): once an event has started it is skipped by every later
	/// bar-exit roll THIS run. Try Again re-arms it (and calls StopEvent) for the next
	/// run; there is still no mid-run stop path by design.
	/// </summary>
	public bool IsStarted { get; internal set; }

	/// <summary>
	/// Begin the event. Called by GameManager's bar-exit roll at most once per run.
	/// Return true if the event actually started; returning false (e.g. payload
	/// component missing from the node - the implementation logs the problem itself)
	/// leaves the event NOT latched, so a later bar exit can roll it again once the
	/// wiring is fixed. Implementations switch on their (authored-disabled) payload;
	/// see DrunkChromaticEvent.
	/// </summary>
	public abstract bool StartEvent();

	/// <summary>
	/// Tear the event down for a new run (GameManager's Try Again resets the run, which
	/// also re-arms IsStarted - without this the payload of the previous run would keep
	/// running into the fresh one). No-op by design for events that self-terminate
	/// (finite DurationSeconds) or have no state to clear; payload-owning events
	/// override (see DrunkChromaticEvent). Never called mid-run.
	/// </summary>
	public virtual void StopEvent() { }
}
