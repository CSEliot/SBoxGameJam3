# InMainMenu Migration Report

Goal: `minimal.scene` becomes the single startup scene; the main menu migrates into it as a
UI panel. `LocalGameState.InMainMenu` becomes a real pre-gamestart state that live code must
respect. This report audits every code/asset site that needs to handle it.

Date: 2026-09-23. All line numbers verified against the working tree (which has uncommitted
edits: the `InMainMenu` enum member, a big `minimal.scene` resave, Hud hint text).

---

## 0. Current disk state (what is already true vs. assumed)

Verified on disk:

- `Code/GameManager.cs:36` — `InMainMenu` enum member EXISTS (uncommitted), but it is dead:
  **nothing ever assigns it and nothing ever reads it.** The field initializer at
  `GameManager.cs:116` is still `LocalGameState.WaitingToStartMinigame`, so on scene load the
  game immediately tries to seat the local player at the starting bar and begin the minigame.
- `sboxgamejam3.sbproj:19` — `"StartupScene": "scenes/mainmenu.scene"` — NOT yet changed.
- `Assets/scenes/minimal.scene` — has NO `Sandbox.UI.MainMenu` component and no UI-MainMenu
  GameObject. Its five ScreenPanels are UI-MiniGame (:449), UI-Esc (:513), UI-HUD (:569),
  UI-BarMenu (:628), UI-EndGame (:678). The migration has not started in the scene.
- `Assets/scenes/minimal.scene:498` — **`"_StartOnPlay": true`** is baked into the Minigame
  panel's scene JSON. Editor-testing flag; see landmine L1.
- `minimal.scene:156-267` — the `--CODE--` node (tag `gamemanager`, NetworkMode 1/Object)
  carries BOTH `Sandbox.GameManager` and the engine's `Sandbox.NetworkHelper`
  (`StartServer: true`, `PlayerPrefab: player/player.prefab`, `SpawnPoints: [SPAWNLOCATION]`).
  NetworkHelper is what actually spawns players today (host-side, on connection activation,
  `NetworkHelper.cs:51-64` in the engine). GameManager's old `SpawnPlayerHelper` is gone;
  only comments/audit notes still reference it.
- `mainmenu.scene` — 3 GameObjects only: Scene Information, a static CameraComponent, and
  UI-MainMenu (ScreenPanel ZIndex 100 + `Sandbox.UI.MainMenu` with
  `_GameScenePath: "scenes/minimal.scene"`). No networking components at all.
- Player spawning today: `NetworkHelper.OnActive(connection)` clones `player.prefab` and
  `NetworkSpawn(channel)` the moment a connection becomes active. `DrunkCC.OnStart` then
  self-fires `OnSpawnHelper` → dresses + registers with `GameManager.OnSpawn`.

---

## 1. The core architecture shift

Old flow: boot → `mainmenu.scene` (no networking, no gameplay components) → PLAY →
`Game.ActiveScene.LoadFromFile("scenes/minimal.scene")` → everything boots "hot" into a
running game.

New flow: boot → `minimal.scene` → lobby exists, bars exist, GameManager exists, but the
local state is `InMainMenu` → PLAY flips state and starts the run **in the same scene**.

Two structural consequences drive most of this report:

1. **The world and its systems are now ALIVE while the menu is up.** Every component in
   minimal.scene (GameManager, Bars, CCCamera, all six UI panels, NetworkHelper) runs its
   OnStart/OnUpdate during InMainMenu. Anything that assumes "scene loaded == game running"
   needs a gate.
2. **Player lifecycle must be decoupled from connection activation.** NetworkHelper spawns a
   running player the instant a connection activates. In the menu that means a drunk citizen
   sprinting around the world behind the menu backdrop (DrunkCC auto-runs; owner-side physics
   in `OnFixedUpdate`). Either defer spawning until game start, or spawn inert and gate
   simulation on state. See section 3.

Bonus (removes a known hazard): `MainMenu.PlayClicked` currently calls
`Game.ActiveScene.LoadFromFile` (`MainMenu.razor:155`). Per the scene-vs-prefab networking
reference, a raw `LoadFromFile` during a network session broadcasts scene-native network
object creates into clients sitting in the wrong scene — the migration deletes this hazard
entirely because no scene load happens mid-session anymore.

---

## 2. GameManager.cs — required changes (the state owner)

| # | Site | Change | Class |
|---|------|--------|-------|
| G1 | `:116` field initializer | `_localGameState = LocalGameState.InMainMenu` — THE change that makes the state real. | REQUIRED |
| G2 | new method, e.g. `StartGameHelper()` / public `HandleMainMenuPlay()` | The only InMainMenu → WaitingToStartMinigame transition. Called by MainMenu panel (push pattern, like BarMenu.Open). Must also trigger spawning of pending players (section 3). | REQUIRED |
| G3 | `OnUpdate:181-182` resolve gate | While InMainMenu (and no local player yet), `ResolveLocalPlayerHelper()` fails and `Log.Error("Couldn't find local player.")` (`:438`) fires **every frame** — menu-duration log flood. Add an InMainMenu early-out BEFORE the resolve, or make the resolve silent while in menu. Same flood comes from Hud/EndGame polling (section 5). | REQUIRED |
| G4 | `OnUpdate:184-187` R-key rescue | Gate on not-InMainMenu (with a deferred player it's unreachable via the resolve gate, but with a spawn-inert player design R would teleport a menu-frozen body). | REQUIRED (cheap) |
| G5 | `OnUpdate:234-244` host uptime block | `_SecondsUptime` / `_StartingBar` re-randomization currently starts as soon as the host's update loop runs. Decide: session clock starts at scene load or at first StartGame? Suggest gating `_startTime` capture to the InMainMenu→start transition so uptime means "game uptime". | DECISION |
| G6 | `OnUpdate:226-232` WaitingToStartMinigame block | No change needed — once G1 lands, this block is unreachable until G2 flips the state. Verify no other path can set WaitingToStartMinigame pre-start (`HandleEndGameTryAgain:509` only runs post-run; `HandlePlayerBarTriggerEnter:537` is PubCrawling-gated). | VERIFY ONLY |
| G7 | `OnUpdate:246` UpdateArrowIndicatorHelper | No-op while the player/arrow is unresolved (null-guarded `:258`). With a spawn-inert design the arrow exists and would point at the target bar during the menu — gate on state or hide with the player. | CONDITIONAL |
| G8 | `INetworkListener.OnActive:476-485` | Already only seeds `_localRandom`/`_serverActive`/`_targetBarWaiting` for the local connection — safe in menu. But it is also the natural hook for the pending-spawn queue (section 3). | EXTEND |
| G9 | `_localGameState` stays non-[Sync] | Correct as-is: each client owns its own menu/start flow. The cross-client "game starts now" signal must be an RPC or per-client local click, not sync of this field (see section 6). | NOTE |
| G10 | Late joiners | A connection activating while the host is mid-game gets a player immediately (host isn't in menu), but the joiner's OWN `_localGameState` is InMainMenu (new default). Their DrunkCC must not simulate until they press PLAY (section 4, D2), or the design must auto-start them (section 6, decision N3). | REQUIRED DESIGN |

---

## 3. Player spawning — the biggest structural decision

`NetworkHelper` (engine, sealed) spawns on `OnActive` unconditionally. Options:

**Option A — deferred spawn queue (recommended).**
Neutralize NetworkHelper's spawner by clearing its `PlayerPrefab` at runtime
(`if (!PlayerPrefab.IsValid()) return;` — `NetworkHelper.cs:53`), keep `StartServer` for
lobby creation. GameManager (already an `INetworkListener`) keeps a
`List<Connection> _pendingSpawns`: OnActive adds; `StartGameHelper()` clones
`_PlayerPrefab` (already a GameManager Property) at `_DefaultSpawnLocation`/SpawnPoints and
`NetworkSpawn(connection)` for each pending, then clears the list. Post-start activations
spawn immediately (host not in menu). Nothing player-shaped exists during the menu — no
DrunkCC/PlayerProgress/PantsDropController/arrow instances at all, which makes sections 4-5
mostly moot for the menu itself (they still matter for the late-joiner case, G10).

**Option B — spawn inert, enable at start.**
Let NetworkHelper spawn as today, but disable the player GameObject (or gate DrunkCC
simulation on state) while InMainMenu; StartGame re-enables. Less new code, but: DrunkCC
OnStart/OnSpawnHelper RPC + dressing all fire during the menu, every menu-only component
needs its own gate, and the world contains a frozen citizen per menu-sitter.

Either way, verify in-editor whether `OnActive` fires for the local connection in a solo
editor playtest (no lobby — `NetworkHelper.OnLoad` early-returns on `Scene.IsEditor`). The
existing log line "IS LOCAL CONNECTION GETTING H CLIENT SETUP HELPER" suggests yes, but the
pending-queue design depends on it. Fallback: StartGame also spawns for `Connection.Local`
if no owned player resolves.

---

## 4. Simulation components — required gates

| File | Site | Issue under InMainMenu | Class |
|------|------|------------------------|-------|
| `DrunkCC.cs` | `OnFixedUpdate:411-451` (owner physics, pants input `:433`, jump, lean) | With Option A: nothing to gate during the menu, BUT the late-joiner case (G10) still needs it: owner is InMainMenu while the body exists. Add a state gate: `_gameManager.GetLocalGameState() == InMainMenu → return` after the IsProxy check (`_gameManager` is resolved in OnSpawnHelper `:842`). | REQUIRED (late-join) |
| `DrunkCC.cs` | `OnUpdate:380-409` (animgraph + camera writes) | Same gate; harmless-but-wrong camera writes (`UseAltTargets/StayBehind`) while menu sits. | REQUIRED with above |
| `PlayerProgress.cs` | `OnUpdate:127-130` Q-key +60s debug | Fires whenever the player exists and is owned, menu included. Gate or accept (debug key). | OPTIONAL |
| `CCCamera.cs` | `OnUpdate:241-244` | Resolve helpers return false silently with no player → camera idles at authored transform. NO error spam, NO change required. (Opportunity: a scenic menu camera is a design choice, not a bug.) | NO-CHANGE |
| `Bar.cs` | `OnStart:52-68`, triggers `:208-225` | GameManager exists in merged scene → `.First()` is safe. Triggers need a player collider; GameManager's PubCrawling gate (`:521`) already swallows stray enter events in every non-crawling state, InMainMenu included. | NO-CHANGE |
| `BarArrowIndicator.cs` | `OnStart:34-38` | Disables itself on proxies; on the owner it only aims when GameManager pushes `PointAt` (already null-guarded). With Option A it doesn't exist in menu. | NO-CHANGE |
| `PantsDropController.cs` | all | Lives on the player prefab; only exists when a player exists. Reads DrunkCC state, never game state. | NO-CHANGE |
| `CollisionReporter / DeleteMe / DeleteIfProxy / SpinMe / BarModules / Extensions` | — | No state awareness needed. | NO-CHANGE |
| `GpuPhysicsPost / GpuRagdoll / LeaderboardEntry` | — | Commented-out / dead code. Ignore. | NO-CHANGE |

---

## 5. UI panels — what renders during InMainMenu

Panel roots render unconditionally; only children inside `@if` gates are skipped. Audit of
what is on screen while the menu should own the screen:

| Panel | Current gate | Behavior in InMainMenu | Class |
|-------|--------------|------------------------|-------|
| **MainMenu.razor** | NONE — root, opaque `.backdrop` (`pointer-events: all`, `#00166D`) and `.content` render always (`MainMenu.razor:40-77`) | On its own scene that was correct; merged into minimal.scene it covers the game FOREVER. Needs: `Visible => _GameManager.GetLocalGameState() == InMainMenu` (new `[Property] GameManager` ref, wired in scene, push/pull like Hud), wrap backdrop+content in `@if (Visible)`, add the state to `BuildHash:178` (currently `ShowCredits, _refreshKey` — a state flip would not repaint). | REQUIRED |
| **MainMenu.razor PlayClicked:148-156** | `Game.ActiveScene.LoadFromFile(_GameScenePath)` | Replace with `_GameManager.StartGameHelper()` (or the Rpc.Host request path, section 6). `_GameScenePath` Property (`:82`, scene-wired) becomes dead — remove it and its scene JSON entry. | REQUIRED |
| **Hud.razor** | Timer/score block gated on RunState (`Hud.razor.cs:89-100`) → hidden in menu (PreRun). BUT `_ShowPantsDebug` defaults **true** (`Hud.razor.cs:59`) and the pants-debug column renders OUTSIDE the Visible gate (`Hud.razor:71-77`) | "NO PLAYER / P: toggle showing pants debug" floats over the main menu. Gate the debug column on not-InMainMenu (needs the same GameManager state read; Hud already has `_GameManager`). | REQUIRED |
| **EscMenu.razor** | F / EscapePressed toggles PAUSED (`:49-70`) | Pressing F in the main menu opens "PAUSED" on top of it. Gate the toggle on not-InMainMenu (add GameManager ref) — or decide F-in-menu opens nothing. Also `LeaveToMainMenu:77-81` is `Game.Close()`; post-migration it COULD mean "flip back to InMainMenu" instead (decision N4). | REQUIRED (gate) + DECISION (leave) |
| **EndGame.razor** | `Visible => RunState == Ended` (`:168`) → hidden in menu | Markup fine. `QuitClicked:263-268` = `Game.Close()` — same decision as EscMenu leave (N4). `OnUpdate:144-155` polls `GetLocalPlayerProgress()` every frame → contributes to the G3 error flood while no player exists. | G3 covers spam; DECISION for Quit |
| **BarMenu.razor** | `IsOpen` pushed by GameManager only from the PubCrawling-gated trigger path | Unreachable in menu. | NO-CHANGE |
| **Minigame** | `IsPlaying` gate | Would be fine EXCEPT scene JSON `_StartOnPlay: true` (minimal.scene:498) auto-`Begin()`s on scene load (`Minigame.Logic.cs:72-76`) → the CHUG overlay + spacebar mug-spawning goes live during the main menu. Clear the flag in the scene. | REQUIRED (data) |

All six panels sit at ScreenPanel ZIndex 100; once MainMenu is state-gated and the debug
column is gated, paint order stops mattering. Don't rely on it.

---

## 6. Networking flow — PLAY, joins, leaving

Facts (from the scene-vs-prefab networking reference, engine-verified):

- A connecting client can never keep its own scene; the host's snapshot replaces it. With
  minimal.scene as the startup scene and `StartServer: true`, joiners land in the host's
  minimal.scene — i.e. **in the menu** while the host is in the menu. This is exactly the
  reference's recommended menu architecture, and it now happens without a scene swap.
- Only the host can drive session-wide changes; a non-host PLAY must route through a
  `static [Rpc.Host]` request (reference's menu Play-button pattern). Static RPCs resolve
  via TypeLibrary and run regardless of scene state — safest channel here. An instance
  `[Rpc.Broadcast]` on GameManager also works post-snapshot (the object exists on every
  client), but static is the reference's recommendation for menu→game transitions.
- The editor's "Join via new instance" connects immediately at boot with no way to defer —
  joined instances will sit in the menu snapshot. Fine under this design.

Required flow design (recommended shape):

1. Host boots minimal.scene → NetworkHelper creates lobby → state InMainMenu → menu shows.
2. Joiners connect → snapshot → their local GameManager also defaults InMainMenu → menu shows.
3. PLAY (any client): `if (Networking.IsHost) StartGameHelper(); else RequestStart();` where
   `RequestStart` is `static [Rpc.Host]`. Host's StartGame: spawn all pending connections
   (section 3), then a `static [Rpc.Broadcast] GameStarted()` (deriving nothing from
   receiver-local fields) so every client flips InMainMenu → WaitingToStartMinigame. Each
   client then runs its OWN existing per-client flow (StartMiniGameHelper seats it at its
   `_targetBarWaiting` bar via the existing `[Rpc.Host]` SitDownPlayer).
   - Simpler alternative: every client flips its own state on its own PLAY click (no
     broadcast at all). Works if "everyone starts together" is not a requirement. DECISION N1.
4. Late joiner mid-game: host spawns their player on activation (host not in menu); joiner
   sees the menu, clicks PLAY, flips locally, DrunkCC gate (D2) releases their body. If
   instead late joiners should skip the menu, the host's spawn path must tell them
   (GameStarted broadcast on spawn). DECISION N2/N3.

Decisions needed from you:

- **N1**: Does one PLAY start the game for everyone (host-authoritative broadcast), or does
  each client start independently when they click?
- **N2**: Do late joiners (host already playing) see the main menu at all?
- **N3**: If yes, does their PLAY need host coordination, or is the local flip + existing
  SitDownPlayer RPC enough? (It is enough for the minigame seat; only spawn timing needs the host.)
- **N4**: "LEAVE TO MAIN MENU" (EscMenu) and "QUIT" (EndGame) — keep `Game.Close()`, or
  become a per-client flip back to InMainMenu? A per-client flip raises: does the player get
  despawned/disabled, does the timer stop, and what does it mean when the HOST does it
  (session ends for everyone? host keeps simulating?). Simplest coherent version: client
  flip-back disables their player + resets run (PlayerProgress.ResetRun) + state InMainMenu;
  host flip-back = offer "end session" instead. Needs your call.

---

## 7. Assets / project files

| File | Change | Class |
|------|--------|-------|
| `sboxgamejam3.sbproj:19` | `"StartupScene": "scenes/minimal.scene"` | REQUIRED |
| `Assets/scenes/minimal.scene` | Add UI-MainMenu node (ScreenPanel ZIndex 100 + `Sandbox.UI.MainMenu`), wire its new GameManager ref; wire GameManager refs into EscMenu (and Hud already has one); clear `_StartOnPlay` (:498); remove MainMenu's dead `_GameScenePath` value | REQUIRED |
| `Assets/scenes/mainmenu.scene` (+ `_c`, `.meta`) | Dead after migration. Keep until the merged flow is verified in-editor, then trash. | LATER |
| `MainMenu.razor:14` header comment | Says "shown on its own dedicated scene (scenes/mainmenu.scene)" — update. | TRIVIAL |
| Stray untracked `Assets/untitled.scene.meta`, `Assets/scenes/dssdasa.scene*` | Unrelated junk; ignore/clean separately. | NOTE |

---

## 8. Landmines checklist (from project skills, applied to this migration)

- **L1 `_StartOnPlay: true` in minimal.scene:498** — the single most likely "menu is broken
  and I don't know why" cause: minigame overlay + spacebar mug clones live during the menu.
  Data-only fix, easy to miss because the C# default is false.
- **L2 Panel roots always render** — MainMenu's opaque backdrop must move inside `@if`, or
  the merged scene shows a permanent blue screen over gameplay (mirror EscMenu/BarMenu's
  backdrop-inside-gate split).
- **L3 BuildHash must include the visibility state** on every panel that gains a state gate
  (MainMenu, Hud debug column), or the panel freezes at its first paint when state flips.
- **L4 Log.Error flood** — three per-frame pollers (GameManager.OnUpdate, Hud.OnUpdate,
  EndGame.OnUpdate) all funnel into `ResolveLocalPlayerHelper`'s "Couldn't find local
  player." error. With deferred spawn this fires for the entire menu session. Fix once in
  ResolveLocalPlayerHelper (quiet while InMainMenu / player legitimately absent).
- **L5 Component lookups skip disabled GameObjects** — if Option B (spawn-inert via
  `Enabled=false`) is chosen, every resolve against the fresh player must pass
  `includeDisabled: true` (GameManager's already does, `:443-446` — this was the BarArrow
  bug); anything new must too.
- **L6 Instance RPCs drop silently if the object isn't in the receiver's active scene** —
  prefer the static [Rpc.Host]/[Rpc.Broadcast] pair for the start signal (section 6).
- **L7 Don't sync `_localGameState`** — it is deliberately per-client; the enum reorder
  (InMainMenu inserted first) is serialization-safe for the same reason.
- **L8 Editor solo playtest** — NetworkHelper.OnLoad skips lobby creation under
  `Scene.IsEditor`; verify OnActive still fires for the local connection (section 3) before
  relying on the pending-queue for the host's own player.
- **L9 EscMenu F-key** — gate in menu, else PAUSED stacks on the main menu (identical
  ZIndex 100 modals, ambiguous click routing — the same class of bug EscMenu's `_BarMenu`
  guard already exists for).

---

## 9. Suggested implementation order

1. sbproj StartupScene + scene data fixes (L1, remove `_GameScenePath` value).
2. GameManager: G1 initializer, G3 quiet-resolve, G2 StartGameHelper (+ spawn approach from
   section 3 — decide A vs B first).
3. MainMenu panel: visibility gate, PLAY → GameManager, backdrop inside `@if`, BuildHash.
4. DrunkCC state gate (D2) + G4/G7 conditionals.
5. EscMenu/Hud gates (L9, pants-debug column).
6. Networking start signal (N1-N3 decisions) + late-join behavior.
7. N4 (leave-to-menu semantics) last — it's the least defined.
8. Gate everything behind the CodeTestPortable compile check (razorgen + dotnet build),
   then verify wiring in the live editor (scene tree / ui_panel_dump via editor MCP), then
   playtest: solo editor, host+join-in-new-instance (menu → PLAY → late join).
