SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

Here's the next step:

	Task 1: DONE? YES!

		In the /3-Research/ folder, there's research about how to implemeent fake beam lights in sbox. Using that info, build fake cop car lights. Ones that spin on the top, and headlamp lights. Do this for each cop car. The spinning color should match their pacman ghost tints.

	Task 2: DONE? NO!

		The cop car lights only turn on when the cop car SEES the player (the same moment the ai car driver logic takes over from the simpler nav mesh pathing logic).
		EDIT: Playtesting is showing the cars not firing the lights. Validate.

	Task 3: DONE? YES!

		The ai car driver logic (NOT the navmesh pathing logic) shouldn't slow down for players. But it does. WE want cop cars to "ram" players.
            
	Task 4: DONE? YES!

		/home/cseliot/1-Development/1-SBox/2-Projects/portablefakelightbeam/ <-- Build a portable code set for this fake light beam inside this project, where it is being demo'd in miniman.scene's "FakeLightSource" gameobject. All code and resources (prefabs, etc) go in /home/cseliot/1-Development/1-SBox/2-Projects/portablefakelightbeam/Assets/FakeLightBeam.

Notes:

Clarification Questions and Answers:

    SESSION START was already YES, so no questions were asked up front.

    Parent decisions (change if wrong):
        - Task 1: each ghost car gets a CopCarLights component that builds its lights at play time: a roof light bar with two back-to-back beacons spinning about the roof (ghost tint, read from the police body's own Tint so it always matches) and two warm white headlamps. Each light is a real SpotLight (shadows off) plus a fake beam cone (FakeLightBeam + new fake_light_beam.shader, design from 3-Research/sbox-fake-light-beam-cone.md). Light positions are inspector offsets with editor gizmos, so they can be tuned in the prefab without playing.
        - The beams are additive, so they show at dusk/night and fade to nothing in bright daylight. That is how the technique works, not a bug.
        - Task 2: "lights" means all of them, beacons and headlamps. They switch on when the car hands control to the AI car driver (line of sight) and off when it hands back to the navmesh. The on/off state is synced so every client sees it.
        - Task 3: in chase mode the AI car driver ignores every player (the chased one, other players, knocked-down ragdolls, and the bike) for all braking, stopping and creeping. Walls, props and other cars still count as obstacles. Outside chase mode nothing changes.
        - Order: Tasks 1 and 3 run in parallel (no shared files), Task 2 after Task 1. Each task is reviewed by a subagent, then committed and pushed on its own. Pushing also pushes the 8 commits already waiting on main.

    Status 2026-10-11: Tasks 1-3 committed and pushed (8e41991, d04a204, b00a6dc).

    Q1 (Task 4): s&box compiles C# only from a project's Code/ folder (engine Project.cs:362 GetCodePath, Project.Compiling.cs:96), so .cs files in Assets/FakeLightBeam/Code would never compile. Where should the C# go?
    A1: Make it an s&box library (Libraries/fakelightbeam with its own .sbproj, Code/ and Assets/), the engine's own drop-in package format.

    Q2 (Task 4): portablefakelightbeam has no git repo of its own (it sits in 2-Projects/, which the workspace repo ignores). Where should the commit go?
    A2: git init in portablefakelightbeam, create a private GitHub repo CSEliot/PortableFakeLightBeam, commit and push.

    Q3 (Task 4): What should the FakeLightSource demo object in minimal.scene show?
    A3: One static spotlight with its fake beam, aimed at the cubes (the core component only).

    Q4 (Task 4): The beams are additive, so they barely show against minimal.scene's bright daylight sky. Darken the scene for the demo?
    A4: Darken minimal.scene to dusk/night (lower the Sun, darker sky) so the beam reads.

    Q5 (Task 4): Optional extras?
    A5: All three: the beam also renders in the editor scene view (not only in play mode); a short README on dropping it into another project; switch sboxgamejam3's cop lights to use the portable copy (one source of truth).

    Parent decisions for Task 4 (change if wrong):
        - Library layout: portablefakelightbeam/Libraries/fakelightbeam/ holds fakelightbeam.sbproj, Code/ (FakeLightBeam component + shared cone model), Assets/ (the shader, a drop-in FakeLightBeam prefab: SpotLight + beam), and README.md at the library root. The empty Assets/FakeLightBeam/Assets and Assets/FakeLightBeam/Code folders you created are not used (A1 moves everything into the library); they are left in place, not deleted.
        - sboxgamejam3 gets the same library under its own Libraries/ folder (copied, the way bugge.vehicle_controller ships), and its Code/FakeLightBeam.cs and Assets/shaders/fake_light_beam.shader are removed so only the library copy exists. CopCarLights keeps working against the library's FakeLightBeam.
        - The demo object FakeLightSource gets the prefab's SpotLight + beam aimed down at the three cubes; the Sun is lowered and the sky darkened in that project's minimal.scene only.

    Q6 (Task 4, sboxgamejam3 switch): The switch needs an editor restart (libraries are scanned only at project load). How to sequence it?
    A6: Apply the swap, the editor is closed. The agent may restart the editor itself via the sbox-agent-playtest skill (first test of that skill).

    Q7: The 14:41-14:44 editor saves of the four ghost prefabs (OnCollideThrow retuned to _ThrowForce 10000) also removed CopCarLights from all four, apparently written back from a copy older than the 10:09 edit. What to do?
    A7: Re-add the four CopCarLights entries (same GUIDs and values as commit 8e41991), keep the OnCollideThrow edits, and commit the four prefabs with both changes.

    Status 2026-10-11: Task 4 done. Portable library + dusk demo pushed to the private repo https://github.com/CSEliot/PortableFakeLightBeam (c0abbc0); beam verified rendering in the editor scene view (Dev editor screenshot). sboxgamejam3 now uses the same library from Libraries/fakelightbeam (old Code/FakeLightBeam.cs and Assets/shaders/fake_light_beam.shader removed), CopCarLights restored in the four ghost prefabs; editor relaunched on sboxgamejam3, compile green.

IGNORE: <<Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
