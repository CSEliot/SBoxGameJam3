SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

Skills for you and subagents:
	box-agent-playtest
	sbox-live-play-agent-loop
	sbox-editor-mcp-driving

Here's the next step:

	Task 0: DONE? NO!
		Write meaningful commits of current git status.

	Task 1: DONE? NO!
		Replace all instances of Razor with Painter. If possible. And without breaking the game.
		Update: Work has been done on this, but there are screen space scaling issues all over the place.

	Task 2: DONE? NO!
		The nav mesh car controller is evaluated, tested, and improved. Current issues: No error state handling (i.e. stuck, upside down, Cars driving off cliffs and into the water, etc). 

	Task 3: DONE? NO!
		Fix: When a police car hits a player, triggering the "throw" on trigger component, the body doesn't return to the player controller on reset. Also, when the player gets thrown, 3 "resets" fight each other: the knockdowner related reset, the turn/lead reset, and the throw-on-police-car-trigger reset. Disable all resets but the throw-on-police-car-trigger reset while that reset is waiting to occur.

	
		
Notes:

Clarification Questions and Answers:

    Q1 (Task 0): player.prefab (saved 2026-10-12 09:59) drops the BikeRiderPose component, which seats the rider on the bike (4af775b). Its wall-hit collider also differs from "Fixing Wall Collision" (ec38abf): the CollisionForWalls node is back at z 9.44 and the collider centre is raised to z 22.35 instead. It looks like the stale editor write-back that wiped CopCarLights before. Intended? (Options: restore BikeRiderPose and keep the new collider values / commit as is / drop the prefab change.)
    A1: Intended

    Q2 (Task 0): Two untracked files: duplifinder.py (fixes case-collision filenames) and print_current.sh (scratch playtest helper, hardcoded Linux path, no .bat twin). Commit, ignore, or leave them?
    A2: ignore.

    Q3 (Task 1): Painter is a 2D drawing API with no clicks, hover or cursor of its own. How far should "replace Razor" go? (Options: all 6 screens become C# Panels that draw with Painter, buttons stay real Panels for input / pure Painter on the camera HUD with hand-written hit-testing / Painter only for Hud and Minigame, menus stay Razor.)
    A3: The "replace Razor" should go as far as possible. Activate skill "sbox-interactable-painter" for more info.

    Q4 (Task 1): How close to the current look? (Options: close match, simple transitions redone in code / functional equivalent / exact match incl. every animation.)
    A4: Same fonts, palette and layout; simple transitions redone in code, fancy ones may be dropped and report if dropped. Though, painter SHOULD allow for everything and more.

    Q5 (Task 1): The HUD's target-bar preview is a live 3D render (ScenePanel); Painter only draws 2D. (Options: keep it as a ScenePanel child / render to texture and draw with Painter / drop it.)
    A5: The frame and text surrounding the HUD is NOT 3d, replace that.

    Q6 (Task 0): The Razor-to-Painter UI work (6 razor screens deleted, 10 new C# files; compiles and runs) is uncommitted and still has the scaling bugs. When should it be committed? (Options: commit now as its own commit, scaling fix on top / hold until Task 1 is fixed, then commit once / separate branch.)
    A6: Hold it uncommitted until Task 1 is fixed, then commit once.

    Q7 (Task 1): Painter draws in screen pixels, while the old Razor/CSS scaled everything by window height / 1080; the current code mixes the two. How should the Painter UI scale? (Options: same as the old Razor UI / fixed 16:9 layout area centred and scaled to fit.)
    A7: Same as the old Razor UI: sizes scale with window height (1080 reference), elements stay anchored to the same screen edges.

    Q8 (Task 2): Ghost cars drive with NavMeshCarController by default and hand off to AICarDriver while they see a player. AICarDriver already has stuck/teleport guards but nothing for cliffs or water. Which controllers get the error-state handling? (Options: both, one shared guard / NavMeshCarController only.)
    A8: Both: one shared error-state guard used by NavMeshCarController and AICarDriver chase mode.

    Q9 (Task 2): What should a car do when it hits an error state (stuck, upside down, off a cliff, in the water)? (Options: self-recover first, teleport if that fails / always teleport back onto the navmesh / destroy and respawn at its scatter corner.)
    A9: Self-recover first (reverse out when stuck, flip upright when on its roof); teleport to the nearest navmesh point on its route if that fails; off the map or in water teleports right away.

    Q10 (Task 2): May players see a ghost car teleport? (Options: prefer out of view, fall back to visible / in view is fine / never in view.)
    A10: Yes, teleporting in view is fine.

    Parent decisions (change if wrong):
    - Task 0: player.prefab is committed as is (A1). duplifinder.py and print_current.sh go into .gitignore (A2). The ghost prefab edits (NavMeshCarController DrawDebug on, PathStrictness 0.50) and the scatter corners raised to z 300 in minimal.scene are committed as authored; the rest of the minimal.scene diff is editor GUID reserialization. The recompiled .shader_c binaries are committed as editor output. Held for Task 1 (A6): the razor deletions, the new Code/UI/*.cs files and the Minigame.Logic.cs comment edit.
    - Task 3: the throw reset keeps its current destination (GameManager.ResetPlayer() to the default spawn, after ResetPlayerOnCopCatchDelay). The R-key rescue is also blocked while the throw reset is pending, since it is a reset too. When the throw reset fires, the ragdoll is turned off and the player is back under the controller.
    - Order: Task 0 first. Then Tasks 1, 2 and 3 run in parallel by file ownership (Task 1: Code/UI; Task 2: NavmeshCarController.cs, AICarDriver*.cs, a new shared guard file, ghost prefabs; Task 3: OnCollideThrow.cs, DrunkCC.cs, GameManager.cs).

IGNORE: <</goal Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
