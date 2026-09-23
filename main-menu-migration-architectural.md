# Main Menu Migration — Architectural Plan

Companion to `InMainMenu-Migration.md` (the audit). This document is the BUILD SPEC: the
decided architecture, the pinned cross-file API contract every task codes against, and the
per-task work orders. Decisions were made 2026-09-23; they supersede the audit's open
questions where they conflict.

---

## 1. Decided architecture

### D-A. Spawn model: Option B — spawn inert

`NetworkHelper` keeps spawning `player.prefab` on connection activation exactly as today
(host-side `OnActive` → `Clone` + `NetworkSpawn(channel)`). No deferred-spawn queue is
built. Instead, the player body is INERT while the local client is in `InMainMenu`:

- `DrunkCC` gates all owner-simulated physics/input on the local GameManager state.
- The body simply rests under gravity at `SPAWNLOCATION` behind the menu's opaque backdrop.
- Accepted costs: `DrunkCC.OnStart` → `OnSpawnHelper` ([Rpc.Broadcast]) and
  `DressPlayerHelper` fire during the menu (dressing RPC traffic while menu sits), and one
  frozen citizen exists per menu-sitter. Both accepted deliberately.

### D-B. Start semantics: per-client start (N1)

`_localGameState` stays NON-synced and per-client. Clicking PLAY flips ONLY the clicking
client's state `InMainMenu → WaitingToStartMinigame`. No group-start broadcast, no
`[Rpc.Host]` start request. Each client then runs its own existing flow
(`StartMiniGameHelper` → `[Rpc.Host] Bar.SitDownPlayer`), which is already per-client and
host-arbitrated at the seat level. Players begin their runs independently.

### D-C. Late joiners (N2/N3): menu, then PLAY

A client connecting mid-host-game gets their player spawned immediately by NetworkHelper
(host-side), owned by them, inert on their machine (their local GameManager defaults to
`InMainMenu`). They see the main menu; their PLAY "sets up the already-spawned DrunkCC"
(releases the gate) and flips their local state. No spawn request needed — the body already
exists. Their minigame seat still goes through the existing host RPC path.

### D-D. N4 deferred: `Game.Close()` stays

`EscMenu.LeaveToMainMenu` and `EndGame.QuitClicked` keep calling `Game.Close()`. The
return-to-InMainMenu flip (player disable/despawn, `PlayerProgress.ResetRun`, host-leaves
implications) is DESIGNED BUT NOT BUILT — see section 5.

### D-E. Scene wiring is done by direct JSON edit

`minimal.scene` is edited as JSON by task T5 (not hand-wired in the editor). Consequence:
the compiled sidecar `minimal.scene_c` goes stale; after the edit, open the scene in the
editor once and save so `_c` regenerates before any packaged/standalone run. Editor
playtests compile from source and are unaffected.

### D-F. Out of scope (explicitly NOT changing)

- `GameManager.OnUpdate` host uptime block (`_SecondsUptime`, `_StartingBar` re-randomize):
  stays as-is; uptime means session uptime, not run uptime.
- `BarArrowIndicator` / `UpdateArrowIndicatorHelper`: no menu gate. The arrow points during
  the menu but is invisible behind the opaque backdrop; not worth a gate.
- `CCCamera`: no change. Its resolvers succeed once the inert player exists; the follow
  behavior runs unseen behind the backdrop.
- `Bar.cs`, `PlayerProgress.cs` (except noted), `PantsDropController.cs`,
  `BarArrowIndicator.cs`, `CollisionReporter.cs`, `DeleteMe.cs`, `DeleteIfProxy.cs`,
  `SpinMe.cs`, `Extensions.cs`: no changes.
- `mainmenu.scene` (+ `_c`, `.meta`): left on disk until the merged flow is verified
  in-editor; deleted (via trash) in a later pass.

---

## 2. Pinned cross-file API contract

Every task codes against EXACTLY these signatures. Nothing else new crosses file boundaries.

```csharp
// GameManager (exists already — do not change signature)
public LocalGameState GetLocalGameState();          // enum: InMainMenu, PubCrawling, AtBarMenu,
                                                    //        WaitingToStartMinigame, PlayingMinigame

// GameManager (NEW — T1)
public void StartGame();
// Valid only while _localGameState == InMainMenu; flips to WaitingToStartMinigame.
// Per-client (D-B): no RPC of any kind inside.
// Solo/editor fallback: if the local player can't be resolved AND !Networking.IsActive,
// Clone(_PlayerPrefab) at _DefaultSpawnLocation (NO NetworkSpawn — it returns false in
// editor scenes anyway). If Networking.IsActive and no player resolves: Log.Error, do not
// clone (NetworkHelper owns spawning in sessions).

// DrunkCC (NEW — T2, private)
private bool InMainMenuHelper();
// Lazily resolves _gameManager like DifficultyBeerHelper does
// (_gameManager ??= Scene.GetAllComponents<GameManager>().FirstOrDefault()).
// Returns false when _gameManager is null (FAIL-OPEN: no GameManager = not in menu;
// InMainMenu is only meaningful when a GameManager exists).
```

UI panels read state via the existing `GetLocalGameState()` through a `[Property] GameManager`
ref, following the Hud/EndGame wiring convention (explicit scene-wired ref, `Log.Warning`
on null in OnStart, feature hidden/disabled when unwired).

---

## 3. Task work orders

### T1 — GameManager.cs (state owner)

File: `Code/GameManager.cs` ONLY.

1. `:116` initializer: `_localGameState = LocalGameState.InMainMenu;`
2. New `public void StartGame()` per contract (section 2), placed near
   `HandleEndGameTryAgain`. Body: guard `if (_localGameState != InMainMenu) return;`,
   solo fallback resolve/clone, then `_localGameState = WaitingToStartMinigame;`. The
   existing `OnUpdate` WaitingToStartMinigame block (`:226-232`) then drives the rest of
   the flow unchanged.
3. `ResolveLocalPlayerHelper` (`:436-440`): while `_localGameState == InMainMenu`, return
   false SILENTLY (no `Log.Error`) — kills the menu-duration per-frame error flood from
   GameManager/Hud/EndGame pollers. Keep the error for all other states. The
   `_LocalPlayerProgress/_LocalDrunkCC/_LocalPlayerRigidbody/_LocalArrowIndicator`
   warnings (`:448-470`) likewise stay quiet in InMainMenu.
4. `OnUpdate` R-key rescue (`:184-187`): gate on `_localGameState != InMainMenu`.
5. Header comment "Description" line: mention the InMainMenu pre-gamestart state.

Do NOT: sync `_localGameState`, add RPCs, touch the uptime block, rename anything.

### T2 — DrunkCC.cs (inert-body gate)

File: `Code/DrunkCC.cs` ONLY.

1. Add `InMainMenuHelper()` per contract.
2. `OnFixedUpdate`: immediately AFTER the existing `if (IsProxy) return;` (`:423-424`) and
   BEFORE the pants `SetPantsState` poll (`:433`), add
   `if (InMainMenuHelper()) return;`. Rationale: owner physics, lean/jump input and pants
   polling all stop during the menu; the grounded/animgraph lines above the IsProxy check
   keep running (they must — proxies blend remote bodies).
3. `OnUpdate`: extend the camera-drive gate (`:388`) to
   `bool driveCamera = !IsProxy && _ccCamera != null && !InMainMenuHelper();` so the
   owner's camera-mode writes (`UseAltTargets`/`StayBehind`) don't run during the menu.
   Animgraph writes stay unconditional (existing comment explains why).
4. `_isGrounded`/history recording: no change (they sit above/below the gates already and
   are harmless inert).

Do NOT: gate proxy paths, touch knockdown/ragdoll logic, change [Sync] members.

### T3 — MainMenu panel (the migrated screen)

Files: `Code/UI/MainMenu.razor`, `Code/UI/MainMenu.razor.scss` (scss likely untouched).

1. Add `[Property] private GameManager _GameManager { get; set; }`; `OnStart` warns
   `"MainMenu: _GameManager is not wired - the menu will stay hidden."` when null
   (Hud/EndGame convention).
2. Add `private bool Visible => _GameManager is not null &&
   _GameManager.GetLocalGameState() == GameManager.LocalGameState.InMainMenu;`
   (unwired = hidden, same fail-closed choice as Hud).
3. Wrap `.backdrop`, `.content` AND the credits `@if (ShowCredits)` block in one
   `@if (Visible) { ... }` — panel roots always render, so the opaque backdrop must be
   inside the gate or it covers gameplay forever (audit landmine L2).
4. `PlayClicked`: replace the `_GameScenePath`/`LoadFromFile` body with
   `_GameManager.StartGame();` (keep a Visible/null guard). DELETE the `_GameScenePath`
   property entirely.
5. `BuildHash`: `HashCode.Combine( Visible, ShowCredits, _refreshKey )` — the state flip
   must repaint (landmine L3).
6. Header comment: replace the "own dedicated scene (scenes/mainmenu.scene)" line with the
   merged-scene reality (lives in minimal.scene, gated on GameManager.LocalGameState.InMainMenu).

Leaderboard code (OnStart/OnUpdate/RefreshLeaderboardAsync): unchanged — it works fine
merged; note it now refreshes on scene load rather than menu-scene load. Acceptable.

### T4 — Hud + EscMenu menu-state gates

Files: `Code/UI/Hud.razor`, `Code/UI/Hud.razor.cs`, `Code/UI/EscMenu.razor`.

1. Hud: the pants-debug column (`Hud.razor:71-77`) renders OUTSIDE the run-Visible gate and
   `_ShowPantsDebug` is `true` in the scene JSON — it would float over the main menu.
   Add to `Hud.razor.cs`:
   `private bool NotInMainMenu => _GameManager?.GetLocalGameState() != GameManager.LocalGameState.InMainMenu;`
   (null GameManager → true, fail-open, preserves today's behavior when unwired).
   Change the razor gate to `@if ( _ShowPantsDebug && NotInMainMenu )` and include
   `NotInMainMenu` in BOTH `BuildHash` branches so the flip repaints.
   The timer/score block needs no change (RunState-gated, PreRun during menu).
2. EscMenu: add `[Property] private GameManager _GameManager { get; set; }` (null-safe like
   its existing `_BarMenu` property — unwired scenes behave exactly as before, fail-open).
   In `OnUpdate`, suppress OPENING while in menu:
   when `_GameManager?.GetLocalGameState() == InMainMenu`, ignore the toggle unless
   `IsOpen` (closing still allowed). This stops PAUSED stacking on the main menu (landmine L9).
3. Update EscMenu header comment to mention the InMainMenu suppression.

Do NOT: touch EndGame.razor (Quit stays `Game.Close()` per D-D; its per-frame poll spam is
fixed by T1.3), touch BarMenu/Minigame code.

### T5 — Project + scene data

Files: `sboxgamejam3.sbproj`, `Assets/scenes/minimal.scene`.

1. sbproj: `"StartupScene": "scenes/minimal.scene"` (line 19).
2. `minimal.scene` (CRLF line endings — preserve; targeted edits only, the file has large
   uncommitted changes, never rewrite whole):
   a. Minigame component `"_StartOnPlay": true` → `false` (~line 498). THE landmine: with
      it true, the CHUG overlay + spacebar mug clones go live during the menu.
   b. EscMenu component (type `Sandbox.UI.EscMenu`, ~line 545): add `"_GameManager"` ref,
      keys stay alphabetical (`_BarMenu` < `_GameManager` < `OnComponent*`), exact shape
      copied from the Hud component's `_GameManager` entry (~line 605):
      `{"_type":"component","component_id":"f80e2d64-81c8-49ec-83fd-9682f3cdaa01","go":"669d3171-c109-4ced-b824-2b431ee9f204","component_type":"GameManager"}`
   c. New top-level `UI-MainMenu` GameObject appended to the `GameObjects` array: mirror
      mainmenu.scene's UI-MainMenu node (ScreenPanel ZIndex 100 Timing AfterPostProcess +
      `Sandbox.UI.MainMenu` with `"_DisplayTopCount": 5`), but:
      - fresh GameObject `__guid` (uuid4); the two component `__guid`s may be reused from
        mainmenu.scene (unique within this scene) or freshly generated — either is fine,
        just no duplicates inside minimal.scene;
      - do NOT include `_GameScenePath` (property deleted in T3);
      - DO include `"_GameManager"` with the same ref shape as (b);
      - `"Tags": ""`, `"NetworkMode": 2` (Snapshot — UI panels are not network objects),
        `"Enabled": true`, same boilerplate as sibling UI nodes.
3. Validate: `python3 -m json.tool Assets/scenes/minimal.scene > /dev/null` parses;
   `python3 -m json.tool sboxgamejam3.sbproj > /dev/null` parses; grep confirms no duplicate
   GUID introduced. Do NOT touch `minimal.scene_c` (regenerated by the editor, see D-E).

---

## 4. Verification plan (parent-run, after all tasks land)

1. Compile gate (CodeTestPortable): `dotnet run -c Release --project razorgen/razorgen.csproj`
   then `dotnet build GameCompileCheck.csproj -c Release`. Proves binding only.
   Run SERIALLY by one process — children must not run it concurrently (shared obj/, and
   razorgen wipes gen/).
2. Scene JSON parse + GUID uniqueness (T5 already runs it; re-verify after merge).
3. Cross-file contract grep: `StartGame(` defined once (GameManager) and called only from
   MainMenu.razor; `InMainMenu` read sites = GameManager, DrunkCC, MainMenu, Hud, EscMenu.
4. Adversarial code review subagent over the combined diff (per standing user preference),
   findings dispositioned on merits.
5. User-side (cannot be automated here): open minimal.scene in the editor (regenerates
   `_c`, confirms the MainMenu node loads without serialization errors), solo playtest
   (menu → PLAY → minigame seat), then host + join-via-new-instance (joiner sees menu,
   PLAY starts their run; late join mid-game sees menu then joins).

Known open risk (verify in step 5): solo EDITOR playtest spawning. `NetworkHelper.OnLoad`
early-returns under `Scene.IsEditor`, so whether `OnActive` fires for the local connection
in a plain editor Play (no hosting) is unverified. T1's solo fallback (`!Networking.IsActive`
→ Clone without NetworkSpawn) covers the no-session case; if editor Play DOES create a
loopback session, NetworkHelper spawns as today and the fallback never triggers. Either
path yields one player; the risk is DOUBLE spawn if both fire. Mitigation built into T1:
the fallback only runs when `ResolveLocalPlayerHelper()` fails at StartGame time.

## 5. Deferred design: return-to-menu (N4, not built now)

For a future pass, the coherent shape: per-client flip back to `InMainMenu` = disable the
player GameObject (or gate stays closed via state), `PlayerProgress.ResetRun()`,
`DrunkCC.BeerLevel = 0`, `_targetBarWaiting = _StartingBar`, re-show menu. Host doing this
must NOT tear down the session for everyone — offer "end session" (destroy lobby) as a
separate explicit action. Player DESPAWN (GameObject destroy + network despawn) vs disable
is the main unresolved detail; disabled-and-inert (mirror of D-A) is the cheaper default.

---

## 6. Implementation status & review disposition (2026-09-23 late)

T1-T5 all landed; compile gate (razorgen + GameCompileCheck) passes with 0 errors (2
pre-existing DrunkCC warnings, both from user WIP hunks). Adversarial review ran over the
in-scope diff; disposition of its findings:

- **Finding 1 (MAJOR) — EscMenu `_GameManager` ref nulled by editor resave: CONFIRMED on
  disk, USER ACTION REQUIRED.** minimal.scene:555 reads `"_GameManager": null` for the
  `Sandbox.UI.EscMenu` component; the file + `.scene_c` were rewritten at 23:49:36 by the
  open editor AFTER T5's JSON edit landed. The other two scene edits survived
  (`_StartOnPlay: false`, UI-MainMenu node with its `_GameManager` ref intact). Likely
  cause: the editor deserialized the scene before the hotloaded assembly exposed the new
  EscMenu property, then saved null over it. FIX IN THE EDITOR (not JSON — the editor owns
  the file now): select UI-Esc, set the EscMenu component's GameManager property to the
  GameManager component on the --CODE-- node, save. Until then the F-key open-suppression
  (T4.2) is dormant (fail-open by design) and PAUSED can stack on the main menu (L9).
- **Finding 2 (MINOR) — fail-closed menu × inert body = soft-lock if the MainMenu panel ref
  ever unwires: ACKNOWLEDGED, NOT FIXED (latent config risk, needs a design decision).**
  The shipping scene has the ref wired; a self-heal/auto-StartGame fallback is a behavior
  change beyond the pinned spec. Candidate mitigations if it ever bites: MainMenu logs
  Error instead of Warning when unwired, or GameManager auto-starts when no MainMenu panel
  exists in the scene.
- **Finding 3 (MINOR) — StartGame flipped state even when the session-active resolve
  failed: ACCEPTED AND FIXED.** StartGame now returns without flipping when
  Networking.IsActive and no local player resolves (late-joiner race window), and the solo
  fallback re-resolves after cloning and also refuses to flip on failure. Menu stays up,
  pollers stay silent, PLAY is retryable. Compile gate re-passed after the fix.
- **Findings 4/5 (INFO) — engine-semantics claims verified, scene JSON otherwise clean
  (65 guids, no duplicates, UI-MainMenu mirrors mainmenu.scene exactly, razor markup
  balanced, BuildHash contracts met, contract grep holds): NO ACTION.**

Remaining before playtest: the editor-side EscMenu wiring above, then section 4's steps
(editor open+save to confirm the MainMenu node loads clean, solo playtest, host +
join-via-new-instance test).
