# Architecture.md

# Play By Play - v0

Minigame Starts.

Local Game State:
InBar = true
MiniGameStarted = true

GameManager tracks and controls renderings of character in pub. Players are allowed to make networked calls, but host is owner of data.

barslots

playerID = locally tracked ID by GameManager

X Player prefabs exist in a line. When EnterBar(playerID) (a global call amongst all clients) is called, the next available Drinker gameobject is enabled, their BarCam is enabled IF LOCAL, clothing set to playing players' clothing, and
when player plays minigame, we spawn Beer at only that BeerSpawnLocation of THAT Drinker/BarCam/BeerSpawnLocation.

## Pants Down

Mechanic (Design Section 1 #13-#16, current): hold Shift to keep the pants up; the
moment Shift is released they slide all the way down to the ankles - slower top speed,
pathetic jump, tighter rotation. Obstacles have NOTHING to do with pants: a collision
only trips you (KnockedDown), regardless of pants state. There are exactly two states
(Up / Down) - the old three-level (waist/knees/ankles) model is gone.

Code state (DrunkCC):
- `PantsState { Up, Down }`, `[Sync] CurrentPantsState` + owner-authoritative
  `SetPantsState()`. Only the STATE replicates, so proxies can mimic the pants
  visuals; the movement tunables never sync (only the owner simulates physics).
- Down-modifiers exist and are read in `OwnerHandleRunningHelper`:
  `_SpeedPantsDownDisabler` (% off top speed, actively strips forward velocity above
  the scaled ceiling - the sphere coasts frictionlessly),
  `_RotationPantsDownIncreaser` (multiplies the target yaw rate -> genuinely tighter
  turns), and `_JumpPantsDownDisabler` (% off `_JumpForce`; 100 ignores the jump press).
- The Shift-hold input IS wired: `OnFixedUpdate` (owner region, after the IsProxy gate)
  polls `Input.Down("Run")` every tick -> `SetPantsState(held ? Up : Down)`, so it tracks
  the hold in both Running and KnockedDown. The action is "Run" (Shift is its KeyboardCode,
  Input.config:39-44); there is no action named "Shift". See TODO.MD "Pants Mechanic".

The clothing system works as follows:
 - Clothes can own as many slots as the creator wants.
 - There are 2 layers (Under and over) per which are the slots relating to parts of the body.
 - The dresser is given a clothing list. Collisions in compatibility result in prioritizing last clothing in list.
 - Clothes can share slots so long as they are different layers. For example, most pants are assigned the groin slot. So too are underwear. But if the underwear is in the Under layer and the Pants the Over layer, they are compatible.

### Implementing In Game
1. When players load in, we backup the default built-in clothes that were set up at design time. 
2. Override our default player model with account data from the player of the following:
 - Skin Tone
 - Clothing
3. Then we apply our own "heart_pattern_boxers" as the final clothing item as layer Over and slots Groin, LeftThigh, LeftKnee, RightThigh,RightKnee. 
 - If the player has something already taking up that slot, we'll also add "trackie_bottoms_black" to give them pants to drop.
4. Finally, we check if they're wearing something full body like a dress. If the clothing owns slots Groin + Chest in the Over layer, it will be removed and we give them "trackie_bottoms_black" to drop.

## Beer visuals (v0 - placeholder)

Beer visuals are NOT fully implemented in v0. The current implementation spawns a
single beer (the `_Mug` prefab, cloned at the Drinker's `BeerSpawnLocation`) as a
stand-in. The final implementation will replace this with a beer-drinking animation
on the character.

## HUD Target-Bar Preview (ScenePanel, built 2026-09-24)

The HUD shows a spinning 3D render of the current target bar bottom-left (Design
Screen 1, "Target Bar Preview"). It is a `ScenePanel` (`<scene @ref>` element in
`Hud.razor`, gated on the HUD's run-`Visible`), which renders a PRIVATE scene
(`ScenePanel.RenderScene`, `WantsSystemScene=false`, engine-owned, destroyed with
the panel) to a texture composited in the UI layer - that is what makes it draw
on top of in-world models; no render-layer flags involved.

Data flow: `GameManager.GetTargetBar()` (read-only accessor over `_Bars[_targetBarWaiting]`,
same target the arrow indicator and enter-ring follow) -> `Bar.ActiveBarModule.Name` ->
matched BY INDEX against Hud's `[Property] string[] _BarPreviewNames` /
`[Property] GameObject[] _BarPreviewPrefabs` (wired on the UI-Hud component in
`minimal.scene`; names are "Garys" / "The Drunken Cam" / "The Spongey Splatoon").
The `-JustModel` prefabs under `Assets/fbx/bars/*/` are the visual-only twins of the
bar modules (model renderers + an authored SpotLight for UI visibility; gameplay
components stripped). Adding a 4th bar means: make its `-JustModel` prefab, then add
ONE ENTRY TO BOTH ARRAYS IN THE SAME INDEX POSITION.

Runtime behavior (all in `Hud.razor.cs`, `UpdateTargetBarPreviewHelper`):
- Rebuild fires only when the `<scene>` panel is recreated (run-gate re-entry nulls
  the @ref; reopening creates a NEW RenderScene - detected via
  `ReferenceEquals( _previewScene, host.RenderScene )`) or the target NAME changes.
  Per-frame BuildHash rebuilds are block-diffed and never destroy the element.
- The clone is made `startEnabled: false`, stripped (Collider/NavMeshArea/BarModule/
  ParticleEffect/Enter-Ring destroyed, NetworkMode=Never recursively), then enabled -
  gameplay components never tick inside the UI scene, not even for one frame.
- Rig: tilt node (static `Rotation.FromRoll(_PreviewKilter)`) > spin node (per-frame
  `Rotation.FromAxis(Vector3.Up, spin)`), camera + key/fill DirectionalLights created
  in-scene; the prefabs' own SpotLight rides along with the clone.
- Auto-frame is a RETRY LOOP (`FramePreviewHelper`, latched by `_previewFramed`):
  `ModelRenderer.Bounds` returns a 16-unit fallback box until the Model resource
  loads, so framing waits until every renderer's `Model != null` and the bounds
  exceed 32 units; spin is held until framed so measurement happens at rest pose.
  Framing: center clone via `LocalPosition = -bounds.Center`, camera distance from
  bounds diagonal / tan(fov/2) x 1.2 margin.
- `cam.IsMainCamera = true` forces synchronous `Scene.UpdateMainCamera()` so
  ScenePanel's RenderScene.Camera branch resolves immediately; `CustomSize` is set
  from the panel box each tick (null CustomSize = full-screen aspect distortion).




Every Minute, the starting seed that is passed to connecting players is changed.




# Score & Timer Architecture - v0 plan

Design references: Design.md Section 1 (#2, #5, #8, #9, #19, #20), Section 2
(Definitions: Bar/Game Over), North Star (points scale with drunkenness), and the
HUD spec in Section 4 (Timer top-left, Score top-center, Drunkenness top-right).

## 0. What already exists (do not re-invent)

- `DrunkCC.BeerLevel` (float, per-player, on the player prefab) is the drunkenness
  value. Bar minigames already feed it: `GameManager.EndMiniGameHelper` runs
  `_LocalDrunkCC.BeerLevel += _minigameController.Beers`. This IS the drunkenness
  meter's backing value - score/timer read it, they do not own a second copy.
- `GameManager` is a scene component present on every client. Its local-loop fields
  (`_localGameState`, `_targetBarWaiting`, `_LocalPlayer`, `_LocalDrunkCC`) are
  per-client: each client's GameManager drives ITS OWN local player. Score/timer
  slot into this same "one local player per client" model.
- `GameManager._SecondsUptime` (`[Sync]`, host) is host wall-clock uptime used only
  for the 60s seed rotation. It is NOT the player timer - leave it alone.
- `LocalGameState { PubCrawling, WaitingToStartMinigame, PlayingMinigame }` already
  marks exactly the transitions the timer must react to.

## 1. Home for the state: a `PlayerProgress` component

Add one component to the player prefab, alongside `DrunkCC`. It holds the per-player
run state and is the single owner of score/timer math:

    Timer          float   seconds remaining in the current run
    Score          float   accumulated points (float; HUD floors to int)
    BarsVisited    int     count, for the Game Over / leaderboard screens
    HasEmergencyBeer bool  one-shot timeout save (Design #20)
    RunState enum { PreRun, Running, Paused, Ended }

Why the player prefab and not GameManager: score/timer are inherently per-player and
other clients must see them (scoreboard, "best drunkard"). Putting them on the owned,
networked player object lets `[Sync]` replicate them for free and matches the existing
`DrunkCC` (per-player, on the prefab) placement. This is the "Sync Player Variables"
TODO item.

## 2. Authority model

Owner-authoritative simulation, host-authoritative finalization:

- `Timer`, `Score`, `BarsVisited`, `HasEmergencyBeer` are `[Sync]` (owner writes,
  everyone reads). Only the owning client ticks them, guarded by ownership
  (`!Network.Active || Network.IsOwner` - the `IsMine` idiom, NOT `!IsProxy`).
  Rationale: each client already runs its own local loop; having the host tick N
  independent countdowns buys nothing and adds latency to the HUD.
- The authoritative leaderboard lives on the host as
  `[Sync(SyncFlags.FromHost)]` state on `GameManager` (host owns the value, all
  clients read). Clients never write it directly.
- Finalization (cash-in / timeout) is reported to the host with `[Rpc.Host]`
  `SubmitScore(...)`; the host appends to the leaderboard. This is the "Server
  Authority on Progression" TODO item.

Trade-off, stated plainly: owner-authoritative `[Sync]` for Timer/Score is trivially
spoofable by a modified client. Accepted for jam scope. Hardening path (not v0): on
`SubmitScore` the host bounds-checks the claimed score against wall-clock elapsed
since the run started times the max plausible points/sec, and rejects/clamps
outliers. Note the host cannot cheaply re-simulate the exact score without also
tracking every bar visit and beer count itself.

## 3. Timer mechanics

Tunables (put on `PlayerProgress` as `[Property]`, these are starting values):

    INITIAL_TIME        120s   Design #2: 2:00 on first bar exit
    HOURGLASS_TIME       +3s   Design Section 1 #10 loot pickup
    EMERGENCY_BEER_TIME +20s   Design #20 auto-consumed on timeout
    SEC_PER_BEER_WON     tune  minigame time reward per beer drunk this round

Lifecycle, hooked into GameManager's EXISTING transition points (GameManager calls
into PlayerProgress; PlayerProgress does not poll GameManager):

- Enter `PubCrawling` for the FIRST time (after the first drink at the starting bar):
  `StartTimer()` -> RunState = Running, Timer = INITIAL_TIME. (Design #2: the timer
  starts when you first LEAVE the bar, i.e. once you re-enter the overworld.)
- Enter `WaitingToStartMinigame` (bar trigger hit,
  `GameManager.HandlePlayerBarTriggerEnter`): `PauseTimer()` -> RunState = Paused.
  Design #45: timer freezes in the bar.
- `EndMiniGameHelper`, before returning to `PubCrawling`:
  `AddTime(won)` where `won = minigameBeersThisRound * SEC_PER_BEER_WON`
  (Design #7/Section 2: reward scales with minigame performance). Then re-entering
  `PubCrawling` calls `ResumeTimer()`. Design #46: won time is added, then resumes.
- Loot pickup (host-authoritative, see the Loot Spawner TODO): host RPCs the owning
  client, which calls `AddTime(HOURGLASS_TIME)`.

Countdown (owner only, while Running): `Timer -= Time.Delta`. On `Timer <= 0`:
`OnTimerZero()`:
  - if `HasEmergencyBeer`: consume it (`HasEmergencyBeer = false`),
    `AddTime(EMERGENCY_BEER_TIME)`, stay Running. Design #19/#20.
  - else: `EndRun(scoreWiped: true)` - Game Over, score lost. Design #19.

## 4. Score mechanics

    BASE_POINTS_PER_SEC   1.0    Design North Star: 1 point/sec baseline
    DRUNK_SCORE_SCALE     tune   how hard drunkenness multiplies points

Accrual (owner only, ONLY while RunState == Running - paused-in-bar earns nothing):

    pointsPerSec = BASE_POINTS_PER_SEC * DrunkMultiplier( DrunkCC.BeerLevel )
    Score += pointsPerSec * Time.Delta

`DrunkMultiplier(beers)` default: `1 + beers * DRUNK_SCORE_SCALE` (linear, no cap,
matching "no maximum drunkenness"). Tiering it later is a curve swap, not a structural
change. Drunkenness reads live from `DrunkCC.BeerLevel`; score does not store beers.

HUD display: `(int)MathF.Floor(Score)`, zero-padded to 4 digits (Design Section 4).

## 5. Endings (Design Section 2: Game Over has two forms)

`EndRun(bool scoreWiped)`:
  - RunState = Ended; stop ticking timer and score.
  - final = scoreWiped ? 0 : (int)Score.
  - Owner calls `[Rpc.Host] SubmitScore(displayName, final, BeerLevel, BarsVisited)`.
  - Show Game Over screen (Design Screen 4): title "GAME OVER" (timeout, orange) vs
    "YOU CASHED IN" (voluntary, yellow).

Triggers:
  - Voluntary cash-in: the Bar Menu "CASH IN SCORE" button -> `EndRun(false)`.
    Design #5a / Section 2.
  - Timeout with no emergency beer: `OnTimerZero` -> `EndRun(true)`.

## 6. Leaderboard (host-authoritative)

`GameManager` (host) holds `[Sync(SyncFlags.FromHost)]` array/list of
`{ name, score, beers, barsVisited }`. `[Rpc.Host] SubmitScore` appends/updates and
keeps it sorted; all clients read it for Design Screen 5. (s&box syncs arrays as a
whole; keep the entry a small struct/record and cap the list length.)

## 7. Edge cases / ordering traps

- Score must NOT accrue during Paused (bar) or PreRun - gate strictly on
  `RunState == Running`.
- `AddTime` must run in `EndMiniGameHelper` BEFORE the state flips back to
  PubCrawling/ResumeTimer, or the first tick of the resumed timer races the reward.
- Everything that mutates Timer/Score/counters must be owner-gated; a proxy client
  ticking these would fight the [Sync] value. Use the `IsMine` idiom, not `!IsProxy`.
- First-drink case: the starting-bar minigame runs while RunState is still PreRun
  (timer not started, no score). Do not treat the starting bar's own trigger as a
  "bar visit" time-pause before the timer has ever started.

# Knockdown Architecture - v0 plan

Design references: Design.md line 85 ("Hit: Anything that causes player to go into
KnockedDown state. Ex: Tilting too much left or right is a 'hit'. Colliding is a
'hit'. Running into wall is a 'hit'."), Design #7/#16 (obstacle hit trips regardless
of pants state), TODO.MD "Implement Tripping / Ragdoll Logic".

## 0. What already exists (do not re-invent)

- `DrunkCC.State { Running, KnockedDown }` and the transition machinery are already
  built and working for ONE trigger: `HandleRunning()`'s "Rule 5" checks measured
  roll against `_MaxHitRoll` (0 disables) every `OnFixedUpdate` tick and calls
  `EnterKnockedDown()` when exceeded.
- `EnterKnockedDown()` / `HandleKnockedDown()` / `ResetFromKnockdown()` are already
  trigger-agnostic: they enable `ShrimpleRagdoll`, wait `_KnockdownRecoveryTime`,
  then stand back up at a position/heading pulled from the recovery history queue
  (see "Recovery History" below - this replaces the v0 `BeerLevel * _SecondsPerBeer`
  seconds-rewind plan). A new knockdown trigger only needs to call the existing
  `EnterKnockedDown()` - none of the recovery plumbing changes.
- Three `DrunkCC` properties already exist for this exact mechanic but are DEAD
  CODE - declared, never read anywhere in `OnFixedUpdate`:
    `_ForwardRayCastPosition`  (float)  - meant as the ray's local forward offset
    `_WillCollideApproachAngle` (float) - meant as the max approach angle (Y)
    `_HasHitObstacle`           (bool)  - meant as a read-only debug/output flag
  Bug to fix while wiring this up: `_WillCollideApproachAngle`'s doc comment is a
  copy-paste of `_HasHitObstacle`'s ("The acute angle at which the forward ray cast
  will trigger a \"hasHitObstacle\" event.") on BOTH properties - it does not
  actually describe `_WillCollideApproachAngle`. Needs a real comment once wired.
  No property currently holds the trace DISTANCE (X below) - one must be added,
  e.g. `_WallHitDistance`.

## 1. Relationship to the pants system (RESOLVED 2026-09-23)

Open question - now decided: pants no longer interact with obstacles AT ALL. The
design changed to hold-Shift-to-keep-pants-up (see "Pants Down" above; Design #5-#7,
#13-#16 updated accordingly), so an obstacle collision NEVER drops pants - it only
trips (KnockedDown), regardless of pants state. This plan's trace/trigger covers the
Hit -> KnockedDown case and nothing else; there is no "pants-drop obstacle-collision
system" left to reconcile or keep separate.

## 2. The mechanic: forward raytrace + distance + approach-angle gate

New Rule (checked every `OnFixedUpdate` tick while `State.Running`, alongside the
existing Rule 5 roll check - first trigger to fire wins, both call the same
`EnterKnockedDown()`):

1. Cast a ray from the body, offset forward by `_ForwardRayCastPosition`, along
   `_Rigidbody.WorldRotation.Forward`, out to length `_WallHitDistance` (X).
   Use `Scene.Trace.Ray(...).IgnoreGameObjectHierarchy(GameObject).Run()`, matching
   the pattern already used by `IsGrounded()`.
2. If the trace misses, no hit this tick - clear `_HasHitObstacle` and stop.
3. If the trace hits, compute the approach angle: the angle between the player's
   forward direction and the wall's inward-facing normal (`-trace.Normal`), i.e.
   `angle = MathF.Acos( Vector3.Dot( forward, -trace.Normal ) ).RadianToDegree()`.
   0 degrees = running straight into the wall face-on; 90 degrees = running
   parallel along the wall (grazing).
4. Gate: trigger knockdown only when `angle <= _WillCollideApproachAngle` (Y, the
   max angle). A small Y means only near-head-on hits count as a Hit; a shallow
   graze along the wall does not knock the player down. Set `_HasHitObstacle = true`
   for this tick either way the trace connects, for debug/animation use, but only
   call `EnterKnockedDown()` when the angle gate also passes.
5. `_WallHitDistance == 0` or `_WillCollideApproachAngle == 0` disables the check,
   matching the existing `_MaxHitRoll == 0` disables-knockdown convention.

## 3. Tunables to add to `DrunkCC`

    _WallHitDistance          float  X: forward trace length (units)
    _WillCollideApproachAngle float  Y: max approach angle (degrees) that still counts as a hit (already declared, needs real doc comment + wiring)

## 4. Edge cases / ordering traps

- Run the wall-raycast check AFTER the Rule 5 roll check (or before - order doesn't
  matter functionally since both just call `EnterKnockedDown()`), but only ONCE per
  tick and only in `State.Running` - `HandleKnockedDown()` already has no forces
  running so re-checking mid-ragdoll is pointless and wasted trace cost.
  `EnterKnockedDown()` is not reentrant-guarded, so don't call it twice in the same
  tick from two different rules; an early `return` after the first hit (as Rule 5
  already does) is required.
- The trace must ignore the player's own hierarchy (sphere collider + ragdoll bones)
  the same way `IsGrounded()` does, or the player will "hit" themselves every tick.
- History recording (`RecordRunningHistoryHelper()`) happens before this check in
  `HandleRunning()`; a wall-triggered knockdown recovers through the same
  Recovery History queue (below) as a roll-triggered one - no separate recovery
  logic needed.

## 5. Recovery History

Recovery History (replaces the v0 seconds-rewind plan; implemented in `DrunkCC.cs`):

Objects:
- `HistorySample`: (`Vector3 Position`, `float Time` - Time is when the sample was
  entered) - plus a `Heading` vector kept from the v0 implementation because recovery
  restores body rotation.
- The queue: a `List<HistorySample>` used as a queue. Bottom (index 0) = oldest
  record, top (last index) = newest.

Exposed `[Properties]`:
- `MaxQueuePositions`: 10 (default). Queue capacity; oldest drops when full.
- `MinDistancePerPosition`: 100 (default, units). Minimum distance travelled from
  the newest recorded position before another is recorded.
- `MinTimePerPosition`: 2 (default, seconds). Minimum time between recorded positions.
- `MaxBeerLevel`: 10 (default, mirroring `GameManager.BeerDifficultyCap`'s default), beer
  level that maps to the BOTTOM of the queue (oldest). Beer
  level 0 maps to the TOP (most recent). Math: (current beer level) / (max beer
  level) = (queue percentage), and (target queue index) / (max queue size) = (queue
  percentage), therefore target index from newest =
  `Clamp(RoundDown((current beer level) / (max beer level) * (max queue size)), 0, (max queue size) - 1)`.

Behavior notes:
- The history is a tail wherein the more a player drinks, the further back they
  recover when entering the knockdown state, both as a punishment and as protection
  for the higher run speed that comes with drunkenness.
- Recording happens only while Running and grounded (airborne/off-map positions
  must never become a stand-up target). A sample is recorded only when BOTH
  minimums pass (time since last record >= `MinTimePerPosition` AND distance from
  newest sample >= `MinDistancePerPosition`); the time gate stays armed while the
  distance gate blocks, so the sample lands the instant spacing is reached.
- On recovery the queue is KEPT (the pre-crash trail survives, so a drunk rewind
  after repeated crashes still lands far back up the route rather than next to the
  wall it would have refilled around) and only the sample timer restarts. The
  recovery position itself is never seeded into the queue: recording has no
  empty-queue bypass and waits a full `MinTimePerPosition` after the timer reset,
  so a knocked-down-again-immediately player stands up where they fell instead of
  snapping back to the same spot twice. Trade-off: after recovering at an older
  entry, the entries between that point and the crash describe the abandoned run
  and remain selectable by later low-beer knockdowns.
- Empty queue at knockdown: fallback to the live transform (stand up where you
  fell). Queue shorter than the target index: clamp to the oldest available.
- The recovery point is still snapped onto the navmesh
  (`SnapRecoveryToNavMeshHelper`, `_RecoveryNavSearchRadius`) as in v0.
- Removed by this rework: `_SecondsPerBeer`, `_RecoveryHistoryHeadroom`,
  `_RecoverySampleInterval` and the time-window trim. The rewind no longer uses
  `DifficultyBeerHelper()`/`BeerDifficultyCap`; `MaxBeerLevel` is the recovery
  system's own saturation and the raw `BeerLevel` feeds the ratio.

