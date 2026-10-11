SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

Here's the next step:

	Task 1: DONE? YES!

		In the /3-Research/ folder, there's research about how to implemeent fake beam lights in sbox. Using that info, build fake cop car lights. Ones that spin on the top, and headlamp lights. Do this for each cop car. The spinning color should match their pacman ghost tints.

	Task 2: DONE? YES!

		The cop car lights only turn on when the cop car SEES the player (the same moment the ai car driver logic takes over from the simpler nav mesh pathing logic).

	Task 3: DONE? YES!

		The ai car driver logic (NOT the navmesh pathing logic) shouldn't slow down for players. But it does. WE want cop cars to "ram" players.
            
Notes:

Clarification Questions and Answers:

    SESSION START was already YES, so no questions were asked up front.

    Parent decisions (change if wrong):
        - Task 1: each ghost car gets a CopCarLights component that builds its lights at play time: a roof light bar with two back-to-back beacons spinning about the roof (ghost tint, read from the police body's own Tint so it always matches) and two warm white headlamps. Each light is a real SpotLight (shadows off) plus a fake beam cone (FakeLightBeam + new fake_light_beam.shader, design from 3-Research/sbox-fake-light-beam-cone.md). Light positions are inspector offsets with editor gizmos, so they can be tuned in the prefab without playing.
        - The beams are additive, so they show at dusk/night and fade to nothing in bright daylight. That is how the technique works, not a bug.
        - Task 2: "lights" means all of them, beacons and headlamps. They switch on when the car hands control to the AI car driver (line of sight) and off when it hands back to the navmesh. The on/off state is synced so every client sees it.
        - Task 3: in chase mode the AI car driver ignores every player (the chased one, other players, knocked-down ragdolls, and the bike) for all braking, stopping and creeping. Walls, props and other cars still count as obstacles. Outside chase mode nothing changes.
        - Order: Tasks 1 and 3 run in parallel (no shared files), Task 2 after Task 1. Each task is reviewed by a subagent, then committed and pushed on its own. Pushing also pushes the 8 commits already waiting on main.

IGNORE: <<Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
