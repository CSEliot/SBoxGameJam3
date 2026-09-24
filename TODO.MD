feedbakc:

camera is shit. tighten it.


## OTHER

 + floor mesh was never 1-1'd with actual floor model
 + add: track last knockdown location. if current knock location is within X distance (expose as property named _DoubleKnockdownDistance) then remove 
    the latest stored recovery history to make player respawn furhter back.
 + re-enable per-character costumes (but everything except for age and IF the clothing covers slots groin and below, either layer over/under.
 + Re-enable dome and collision mesh upon game start.
 + RECOVERY NEEDS TO TAKE INTO ACCOUNT NEW BEER LEVELS
 + Arrow falls like physical object on knockdown.

### Art
 + Add color to bar glow shader 

### Character Controller & Physics

 + upon exit of bar, when spawning player, if spawns inside existing player, those 2 cant collide.
 - - upon exit of bar: Add "noncollidable" tag to exitting character for 2 seconds. cc-body && obstacles can't interact w each other.
  + Add "Safe Respawn" button in settings that TP's to nearest gameobject w tag: "safe-point".
 + Handle for edge case where we have more players at a bar than drinkers.
 + Review design info regarding mini-game, ai got it VERY wrong.

### DrunkardController

- [ ] Implement auto-forward movement.
- [ ] Add logic to slowly build momentum (lerp speed from 0 to MaxSpeed over $X$ seconds).
- [ ] Link character top speed to drunkenness level.
- [ ] Add character tilt, so the character doesn't move in a straight line by default, tie the tilt to the drunkenness level.

### Pants Mechanic (hold Shift)

Design: hold Shift to keep pants up; released = pants at ankles (slower, pathetic jump,
tighter rotation). Obstacles do NOT drop pants - they only trip (see below).

- [x] Binary `PantsState` on DrunkCC - `[Sync] CurrentPantsState` + owner-authoritative
  `SetPantsState()`. Only the STATE replicates (for proxy visuals); tunables never sync.
- [x] Drive it from Shift-hold: `Input.Down("Run")` -> Up, released -> Down. Polled every
  owner tick in OnFixedUpdate (continuous hold, works in Running AND KnockedDown). The bound
  action is "Run" (Shift is its KeyboardCode, Input.config:39-44) - there is no action named
  "Shift", so `Input.Down("Shift")` would warn + always read false.
- [x] Speed modifier while Down - `_SpeedPantsDownDisabler` (% off top speed; scales the
  ceiling AND actively strips forward velocity above it, since the sphere coasts on a
  frictionless surface). Currently defaults to 0 - needs tuning.
- [x] Rotation modifier while Down - `_RotationPantsDownIncreaser` multiplies the target
  yaw rate, so turning is genuinely (1+Y)x faster/tighter. Currently defaults to 1 (2x).
- [x] Jump-height cut while Down - `_JumpPantsDownDisabler` (0-100%) scales `_JumpForce`;
  100 ignores the jump press entirely (no force, no anim trigger, no gate latch).

### Implement Tripping / Ragdoll Logic

- [x] Obstacle hit = trip REGARDLESS of pants state - `CollisionReporter` wall-hit
  trigger sets `_HasHitObstacle`, consumed by the Running-state check ->
  `OwnerEnterKnockedDownHelper()` (first hit wins vs. the lean-over trip).
- [x] Temporarily disable movement input and zero out current velocity - no forces run
  while KnockedDown; recovery (`ResetFromKnockdownHelper`) stands up at zero
  velocity/angular velocity at the beer-rewound history point.
- [x] 1-2 second lockout timer followed by automatic recovery/get-up -
  `_KnockdownRecoveryTime` via `_knockdownEnds` (TimeUntil), then auto-reset.

### Implement Unstuck / Rescue System

- [ ] Bind a specific key/button for "Rescue".
- [ ] Calculate a valid fallback position.
- [ ] Zero out velocity and teleport the player upon activation.

## Game Loop & State Management

### Create GameManager / GameState Component

- [ ] Handle player spawning at random valid "Starting Bar" points.
- [ ] Track total player score and active state.
- [ ] Track "Emergency Beer" state.

### Implement Player Timer System

- [ ] Create a countdown timer (starts at 2:00 when leaving the first bar).
- [ ] Add methods for `PauseTimer()`, `ResumeTimer()`, and `AddExtraTime(float seconds)`.
- [ ] Add a listener for OnTimerZero:
  - Check inventory for "Emergency Beer".
  - If true -> consume item, add $X$ time.
  - If false -> trigger Game Over / Score Wipe sequence.

### Create Bar Visit Logic

- [ ] Define a list of all unvisited bars for the player.
- [ ] Update the list locally as the player visits each trigger zone.
- [ ] Create a trigger zone around the bar to auto-enter the when in zone.
- [ ] Call `AddExtraTime` on every bar visit to add the time won in the mini-game.

## World & Entities

### Develop Loot Spawner Manager (Server-Side)

- [ ] Create a spawn manager for "Hourglass" (+3 sec beer bottles) and "Emergency Beer" (+20 sec) items.
- [ ] Ensure logic runs only on the Server to prevent duplicate grabs (First-come, first-serve).
- [ ] Destroy entity and RPC the client to add time/inventory upon pickup.
- [ ] Ensure a max of only one "Emergency Beer" per player.
- [ ] Don't destroy "Emergency Beer" item on collision if the player colliding with it already has it.

## Navigation & UI

### Implement Dynamic Arrow Indicator

- [ ] Create a 3D UI pointer.
- [ ] Calculate distances to all unvisited bars:
  - If $> 4$ bars remain: sort by distance, drop the top 3 closest, point to the 4th.
  - If $\le 4$ bars remain: point to the absolute closest.

### Build Bar Menu UI

- [ ] Create a modal window with two buttons: [Cash-in Score] and [Play Mini-game].
- [ ] Call `PauseTimer` when entering the bar trigger zone.
- [ ] Map "Cash-in" to the game-over/score-saving logic

#### Develop Spacebar Mini-Game

- [ ] Create UI for the mini-game (e.g., a progress bar or rhythm indicator).
- [ ] Listen for rapid spacebar inputs over a short duration.
- [ ] Calculate a "performance score" and map it to a time reward / drunkenness multiplier.
- [ ] Apply the won time and trigger `ResumeTimer()` upon exit.

### Build Player HUD

- [ ] Display current Timer, Score, and Drunkenness level.
- [ ] Show an icon if the player is carrying an Emergency Beer.
- [ ] Visual indicator for pants state (Up while Shift held / Down otherwise).

## Animation & Visuals

### Configure Animator / Blend Trees

- [ ] Add the "Tripping over" and "Getting up" animation sequences.
- [ ] Create separate movement animations for the two pants states (e.g. a normal run when Up, a waddle/stumble when Down).

### Dynamic Pants Visuals

- [ ] Drop the worn pants to the ankles / pull them back up visually, driven by `CurrentPantsState` (see Pants-Drop.md - shader slide + bodygroup plan, knockdown flying-pants prefab).

## Networking

### Sync Player Variables

- [x] [Sync] `CurrentPantsState` (Up/Down) and `CurrentState` (Running/KnockedDown) so proxies show pants/ragdoll. Still open: `BeerLevel` and current speed are not synced, so remote drunkenness/velocity don't replicate.

### Server Authority on Progression

- [ ] Ensure the final Score Cash-in and Item Pickups are validated on the server to prevent cheating.
- [ ] Mini-game results should be sent from Client to Server via RPC for validation before adding time.

## Known Limitations

+ Extensions.ApplyClothingOnlyAsync (account clothing on players/drinkers) mitigates the race with Dresser's own OnAwake auto-Apply() via CancelDressing(), but that only cancels the token-checked/not-yet-started portion. Dresser.Apply()'s synchronous work (Reset + garment recreation) can't be interrupted once started - a true fix needs an IsDressing/generation-counter guard added at the Dresser level itself, which is out of scope for a project-side extension method.
