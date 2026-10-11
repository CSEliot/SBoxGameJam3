SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

Here's the next step:

Task 1: DONE? YES!

	The LLM Agent is able to test sbox editor it's work by itself. Design and build a portable SBOX-LLM-Playtest System. This system will be a combination of instructions for llms, telemetry that's easy for llms to cherry-pick (because llms have strict word limits), and a method of faking input that's not invasive to the host pc.
	This system should NOT perfectly replicate a human player. It should be short and simple. KISS.


Task 2: DONE? YES!

	The pacman cars actually spawn (I played up until reaching beer 100+ (though, that beer was added artifically via L button)) and the only car that spawned was the ai_car. This is a bug that needs fixing.

Task 3: DONE? YES!

	There is now a bike that the player rides. We have no "bike riding" animation. This is a problem to be solved without animation. I've added ik targets on the bike for the hands and feet, perhaps that is helpful. If possible, make the player model, instead of running and walking,
	use the bike. Colliders and all else the same, this is just a visual change.

Task 4: DONE? YES!

	Let's replace the ghosts' muscle car model with the police car model. i've already added the police prefab to each of the ghost prefabs. Replace the muscle car w the police car, including things like tint and any references in the prefab to the muscle car must be replaced.
            
Notes:

Clarification Questions and Answers:

    Q1 (Task 1): Where should the playtest system live so it stays portable?
    A1: A self-contained folder in this project (one or two C# components plus one markdown guide for LLMs) that can be copied into any s&box project.

    Q2 (Task 1): s&box has a public Input.SetAction(action, down), so a component can press game actions (Left, Right, Run, Jump) without touching the OS; the agent writes a short command file the component plays back. Raw key reads (L, R, minigame space) and the PLAY button can't be faked that way. What may the agent do?
    A2: Input plus a few shortcuts: start the run from the menu, set beers, teleport, so a test reaches the state it needs in seconds.

    Q3 (Task 2): What exactly did you see?
    A3: The plain ai_car look, over the most recent several playthroughs. I was also watching the hierarchy for the others to spawn, which they didn't.

    Q4 (Task 3): When the player is knocked down and ragdolls, what should the bike do?
    A4: Rider ragdolls off; the bike stays with the player root as it does now.

    Parent decisions (change if wrong):
        - Task 1 is built first. Commands and telemetry go through files on disk (same transport as the AI car telemetry), so it works without the editor MCP; the MCP is only used to start/stop play and take screenshots. The agent starts and stops play itself only while the editor is idle (not already playing).
        - Telemetry per run: a short summary, one line per event, one sample line per second, so an LLM can grep or read only the tail.
        - Task 2 is reproduced with the Task 1 system before it is fixed. Two symptoms to explain: no ghost colors, and only one car.
        - Task 3: seated pose with the bike's hand/foot targets via the citizen anim graph's IK if it works with the current body setup; otherwise pose the limbs in code. Checked with screenshots.

    Q5 (Task 2, OPEN - timed out): Reproduction in the Dev editor spawned 0, 1, 2, 3, 4 ghost cars at 0, 3, 6, 9, 12 beers, each with its ghost tint set on the body renderer. Which editor were your playthroughs in: the Steam s&box editor (Proton), the Dev build editor (the one the MCP talks to), or both?
    A5: I was mistaken. The cars DO spawn. I as msitaken :) Good catch!

    Q6 (Task 2, OPEN - timed out): The ghost tint multiplies the car texture and the muscle car's paint is nearly black, so only the light racing stripes turn red: in daylight Blinky reads as a black car with red stripes, at night as a dark car (screenshot: cache/scratch/gj3-new/live3/blinky.png). How should the ghosts be colored: a solid-color material per ghost, the tint plus a colored glow/light, or leave it?
    A6: Forget about this, the colors are fine!

    Q7 (Task 1, OPEN - timed out): A fresh run from `shortcut start` ends about 30 s later when the run timer hits 0, which cuts every test short. Add a fourth shortcut `time <seconds>` that adds run time?
    A7: I currently press Q to add time (for debug). If you can't use that mechanism add your own.

    Q8 (Task 3, OPEN - timed out): With IK active the rider sits, but the bike puts the grips beyond his reach from the saddle. Screenshots in /home/cseliot/.hermes/profiles/hermes-sbox/cache/scratch/gj3-new/probe/: m_left.png (bike as authored: hips on the saddle, hands about 15 units short) and b17_left.png (bike moved back to local X 17: hands on the grips, rider sits just in front of the saddle). Move the bike back, keep it, or move the hand targets back toward the rider?
    A8: This looks fine for now. Great job! We'll work on fine tuning the look later. This is good enough.

    Parent decisions after A5-A8 (change if wrong):
        - Task 2: no bug to fix (A5, A6); closed with no code change.
        - Task 1: Q is a raw keyboard read (PlayerProgress.cs:127), which the playtest system can't fake, so it gets its own `shortcut time [seconds]` (default 60, same as Q) that calls PlayerProgress.AddTime.
        - Task 3: the bike stays where you placed it (A8 read as "keep it as is").

    Q9 (Task 3, OPEN - timed out): The rider pose depends on your uncommitted bike work (the bike node and its hand/foot targets in Assets/Player/player.prefab, plus the untracked Assets/Player/bikes/ folder). Committing only the Task 3 pieces would leave the repo pointing at a bike that isn't committed. Should the Task 3 commit contain (a) Code/BikeRiderPose.cs + the whole player.prefab + Assets/Player/bikes/, (b) BikeRiderPose.cs + the whole player.prefab only, or (c) nothing, you commit it with your bike work?
    A9: Answer A. Include everything in the /Bikes/ folder.

    Status 2026-10-11: Task 1 committed as f67f28b (live-checked 05:39 in the Dev editor), Task 3 as 4af775b. Their DONE marks were set at 02:50 and came back as NO with the 03:08 save that added Task 4; set to YES again.

    Parent decisions for Task 4 (change if wrong):
        - Each ghost prefab: the police instance takes over everything the muscle instance had: that ghost's tint on the body renderer, its ModelCollider and four wheel SphereColliders disabled (the car root's own colliders do the physics), wheel positions, and the VehicleController wheel Visual references. Then the muscle instance is deleted.
        - The police wheel models get their pivot centred (align_origin BoundsCenter), exactly like the muscle wheel models already are; VehicleController moves and spins each wheel visual around its pivot. Only the ghost prefabs use the police car.
        - Car physics (colliders, wheel radius, springs) stay as they are. ai_car.prefab is not touched.

    Q10 (Task 4): The ghosts render as tinted police cars, but the wheels don't sit in the wheel wells: the physics puts each wheel at the original car's spring spots, and the police car is smaller (its wheels sit about 8 units further in front/back and 6 at the sides). Scale the police model up about 15%, move the springs to the police wheel spots, or leave it?
    A10: Scale the police model up about 15% so its wheel wells line up with the existing wheels (visual only; driving unchanged, ghost cars look a bit bigger).

    Q11 (Task 4): Since the swap, the ghost prefabs got new tags, reworked colliders, and ghost_blinky an OnCollideThrow component (Code/OnCollideThrow.cs staged, never committed). What goes in the Task 4 commit?
    A11: The 4 ghost prefabs as they are now + the 4 police wheel models + Code/OnCollideThrow.cs (working-copy version).

IGNORE: <<Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
