# SBOX-LLM-Playtest

A tiny self-contained harness so an LLM agent can playtest its own work in the s&box editor: it fakes
input through the engine's own action bits (no OS key injection, the host keyboard is untouched) and
writes short, grep-friendly telemetry files the agent reads back. NOT a human-player replica: no
mouse, no raw keys.

Files (`Code/LLMPlaytest/`):
- `LLMPlaytest.cs` - generic half, identical in every project: a `GameObjectSystem<LLMPlaytest>`,
  auto-created per scene, idle unless this scene is the game's MAIN scene during an editor-run play
  session (not the edit scene, not a UI-owned render scene such as the HUD target-bar preview).
- `LLMPlaytest.Project.cs` - project half: shortcut handlers + extra telemetry fields; the ONLY file
  rewritten when copying the folder to another project.
- `README.md` - this guide.

## 1. Preconditions
- Editor running with MCP at `http://127.0.0.1:7269/mcp`, plus the `play` (`start`/`stop`) and `editor_status` tools.
- NEVER start/stop play while the user is playing. If `editor_status` shows `IsPlaying` on arrival, that is the user's session - read telemetry only, do not stop it. Never save while playing.

## 2. The loop
1. `editor_status` -> note `IsPlaying` and the data path `<engine>/data/<org>/<ident>#local/llm-playtest/` (`<ident>` is the package id, here `bottomsup`).
2. If not already playing, `play start`.
3. Wait for `llm-playtest/current.txt` to change (written when the play scene begins); its value is the session id `<value>`, and the session folder is `llm-playtest/<value>/`.
4. ONLY THEN write `llm-playtest/commands.txt` atomically (write `.tmp`, rename over). First line MUST be `run <id>`; change `<id>` every new script.
5. Sleep, then read the WHOLE `summary.txt` and `tail -n 20 events.txt`; grep `events.txt` / `samples.csv` for specifics.
6. When done, stop play ONLY if you started it.

Exactly ONE session is live per play session: the harness starts a session only in the game's main
scene, so the HUD target-bar preview (a `ScenePanel` render scene) no longer opens a second,
playerless session that overwrites `current.txt`. A `commands.txt` present at play start is MOVED to
`stale-commands.txt` and NEVER run. A scene reload mid-play restarts the session; avoid reloads.

## 3. Commands
Scripts run top to bottom, one at a time; `#` lines and blanks are ignored. Each command logs a `cmd` event then `done` or `error`.

    run <id>                     header (required first line; new id = new script)
    down <Action>                press and HOLD (does not block)
    up <Action>                  release a held action
    hold <Action> <seconds>      hold N s and BLOCK until it expires, then release
    press <Action>               one press: seen by both OnUpdate and OnFixedUpdate readers
    wait <seconds>               block for N seconds
    shortcut <name> [args...]    project hook (section 4)
    mark <text>                  write a `mark text=...` event

`<Action>` is an input ACTION name from `ProjectSettings/Input.config` (`Run`, `Jump`, `Left`, `Right`), not a key name. Unknown actions get an `error` event. Example:

    run demo-1
    shortcut start
    shortcut beers 15
    down Run
    hold Left 2.5
    up Run
    mark finished-run

## 4. Shortcuts (this project)
    shortcut start                 GameManager.StartGame(); BLOCKS until state is PubCrawling (`shortcut start from=.. to=..`), or 30 s -> `error shortcut start reason=timeout sticky=<state>`
    shortcut beers <n>             local DrunkCC.BeerLevel = n
    shortcut teleport <x> <y> <z>  local player root world pos; zeroes velocity, clears interpolation
    shortcut time [seconds]        local PlayerProgress.AddTime(seconds); default 60 (the Q debug key's +60 s)
Shortcuts are cheats by design: they poke local game state directly.

## 5. Telemetry
- `summary.txt` - <= 25 lines, rewritten whole every 1 s (tiny, may lag events.txt): session, t, scene, last script id + status, one `key: value` per current field, `events`/`samples` counts, then the last few events.
- `events.txt` - one `t=12.3 <kind> key=value ...` per line. Kinds: `session`, `script`, `cmd`, `done`, `error`, `mark`, `shortcut`, `state`, `player`, `beers`, `run`, `car+`, `car-`. APPEND-ONLY.
- `samples.csv` - one row per second. Columns: `t` (s since session start), `px py pz` (player world pos), `speed` (rigidbody speed), `yaw` (player yaw), `state` (GameManager local state), `player` (DrunkCC state), `beers` (BeerLevel), `pants` (DrunkCC pants state), `cars` (live car count: ghost cars plus AI cars without a ghost brain), `carnames` (names joined with `|`), `run` (PlayerProgress: PreRun/Running/Paused/Ended), `timer` (PlayerProgress seconds left). APPEND-ONLY.

`events.txt` and `samples.csv` are APPEND-ONLY: each flush appends only lines since the last, so a mid-flush read only ever yields an INCOMPLETE FINAL LINE, never a truncated file - if the last line looks cut off, read again. No row caps. A run ends when `timer` hits 0 and `run` becomes `Ended`.

    tail -n 20 llm-playtest/<session>/events.txt
    grep ' car+ '  llm-playtest/<session>/events.txt
    grep ' error ' llm-playtest/<session>/events.txt
    awk -F, '$5>300' llm-playtest/<session>/samples.csv      # rows where speed > 300

## 6. Notes
- A `shortcut start` run lasts only ~30 s before the timer puts `run` into `Ended`. Do time-critical checks right after `start`, not after long `wait`s. Add `shortcut time 600` right after `shortcut start` to keep a test run alive.
- Scene lighting darkens as the leading player's beer count climbs (night falls). Do colour/visual checks at LOW beers, or reset first with `shortcut beers 0`.

## 7. Visual checks
Screenshot over the MCP instead of inferring from telemetry. During play, create a temporary
`GameObject` with a `CameraComponent` near the target (`create_game_object` / `add_component`), aim it
with `WorldRotation`, then `camera_screenshot` with its id. It lives only in the running play scene
and is discarded on play stop - never save the scene.

## 8. Limits
- No raw-key faking and no mouse; only named ACTIONS are injected, via `Input.SetAction`.
- Not a human replica: it drives the action bits the game reads, nothing more.
- A held action reads `Down` every frame/step but `Pressed` only on its first step (the harness sets the last-action bit after).
- Shortcuts are cheats by design.

## 9. Porting
1. Copy the whole `Code/LLMPlaytest/` folder in.
2. Rewrite `LLMPlaytest.Project.cs` only: shortcut handlers and sample/event fields; public APIs only.
3. Adjust `PlayerTag` (top of `LLMPlaytest.cs`) if the project tags its player root differently.
4. Main scene = `Scene == Game.ActiveScene && Scene.WantsSystemScene` (see `EnsureSessionHelper`). If a project authors its main scene with `WantsSystemScene = false`, flip that field or adjust the check.
5. Keep the project sample fields a STABLE set of names (use `-` when unavailable) so the CSV header stays fixed. The generic half needs no edits.

## Input-injection timing (why OnUpdate AND OnFixedUpdate both see it)
The engine keeps the per-frame and per-fixed-step action bits in SEPARATE contexts, each rebuilt independently, so the harness sets input in two hooks: `Stage.StartUpdate` (frame context pushed at the top of the client frame, signalled before OnUpdate - this frame's OnUpdate sees the bit, re-applied each frame) and `Stage.StartFixedUpdate` (fixed context pushed per step, signalled before OnFixedUpdate, re-applied every step). A `press` is applied once in the frame context AND queued for the fixed context, consumed on the next fixed step that actually runs - so a frame with zero or several fixed steps never drops the press.
