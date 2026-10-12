SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

Here's the next step:

	Task 0: DONE? NO!
		Write meaningful commits of current git status.

	Task 1: DONE? NO!
		Replace all instances of Razor with Painter. If possible. And without breaking the game.

Notes:

Clarification Questions and Answers:

    Q1 (Task 0): player.prefab (saved 2026-10-12 09:59) drops the BikeRiderPose component, which seats the rider on the bike (4af775b). Its wall-hit collider also differs from "Fixing Wall Collision" (ec38abf): the CollisionForWalls node is back at z 9.44 and the collider centre is raised to z 22.35 instead. It looks like the stale editor write-back that wiped CopCarLights before. Intended? (Options: restore BikeRiderPose and keep the new collider values / commit as is / drop the prefab change.)
    A1: No answer (question timed out). Parent decision: player.prefab stays uncommitted until answered, so nothing is lost or guessed. Task 0 stays NO until then.

    Q2 (Task 0): Two untracked files: duplifinder.py (fixes case-collision filenames) and print_current.sh (scratch playtest helper, hardcoded Linux path, no .bat twin). Commit, ignore, or leave them?
    A2: No answer (question timed out). Parent decision: both stay untracked until answered.

    Q3 (Task 1): Painter is a 2D drawing API with no clicks, hover or cursor of its own. How far should "replace Razor" go? (Options: all 6 screens become C# Panels that draw with Painter, buttons stay real Panels for input / pure Painter on the camera HUD with hand-written hit-testing / Painter only for Hud and Minigame, menus stay Razor.)
    A3: No answer (question timed out). Parent decision (change if wrong): all 6 screens (Hud, Minigame, MainMenu, BarMenu, EscMenu, EndGame) lose their .razor/.scss. Each becomes a plain C# PanelComponent that builds its panels in code and draws its visuals with Painter (OnDraw). Buttons stay real Panels so clicks, hover and the cursor keep working. Class names, namespaces and [Property] names stay the same, so minimal.scene needs no edits.

    Q4 (Task 1): How close to the current look? (Options: close match, simple transitions redone in code / functional equivalent / exact match incl. every animation.)
    A4: No answer (question timed out). Parent decision (change if wrong): close match. Same fonts, palette and layout; simple transitions redone in code, fancy ones may be dropped.

    Q5 (Task 1): The HUD's target-bar preview is a live 3D render (ScenePanel); Painter only draws 2D. (Options: keep it as a ScenePanel child / render to texture and draw with Painter / drop it.)
    A5: No answer (question timed out). Parent decision (change if wrong): keep it as a ScenePanel child inside the code-built HUD panel.

IGNORE: <<Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
