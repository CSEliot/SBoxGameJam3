## Character Controller & Physics

 - camera needs to quickly flip to back of cc
 - recovery to nearest navmesh vertex wasn't working
 - camera jerking
 - recovery can still get stuck if stuck in same place for more than 10 seconds. So need to be node-or distance based? or recovery doesn't replace old positions until certain distance.
 - + perhaps manual recovery locations?

### DrunkardController

- [ ] Implement auto-forward movement.
- [ ] Add logic to slowly build momentum (lerp speed from 0 to MaxSpeed over $X$ seconds).
- [ ] Link character top speed to drunkenness level.
- [ ] Add character tilt, so the character doesn't move in a straight line by default, tie the tilt to the drunkenness level.

### Pants State Machine

- [ ] Create PantsLevel to manage 3 levels of pants state.
- [ ] Apply speed and jump power modifiers based on the current PantsLevel.
- [ ] Trigger a pants-drop event on obstacle collision.
- [ ] Implement pulling pants up mechanic.

### Implement Tripping / Ragdoll Logic

- [ ] If obstacle hit while `PantsLevel == Ankles`, trigger a trip state.
- [ ] Temporarily disable movement input and zero out current velocity.
- [ ] Implement a 1-2 second lockout timer, followed by an automatic recovery/get-up state.

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
- [ ] Visual indicator for current Pants Level.

## Animation & Visuals

### Configure Animator / Blend Trees

- [ ] Add the "Tripping over" and "Getting up" animation sequences.
- [ ] Create separate movement animations for different pants levels (e.g., a normal run, a waddle, a stumble).

### Dynamic Pants Visuals

- [ ] Use Skinned Mesh bodygroups or bone-scaling to visually drop the pants to the knees and ankles.

## Networking

### Sync Player Variables

- [ ] Use [Sync] properties for variables that other players need to see (e.g., PantsLevel, visual drunkenness, current speed).

### Server Authority on Progression

- [ ] Ensure the final Score Cash-in and Item Pickups are validated on the server to prevent cheating.
- [ ] Mini-game results should be sent from Client to Server via RPC for validation before adding time.
