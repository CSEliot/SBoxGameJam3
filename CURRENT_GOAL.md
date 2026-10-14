SESSION START? YES!

Hello! Thank you for your time we're so excited you're working for us! Happy happy!

All tasks are DONE and WORKED ON by SUBAGENTS. PEER Review is ALSO done by SUBAGENTS. You are simply their task master.

Upon the completion of each task, a SUBAGENT shall write a git commit and push.

References to variables like X and Y automatically imply that these are to be editor exposed properties for developer tweaking.

Skills for you and subagents:
	box-agent-playtest
	sbox-live-play-agent-loop
	sbox-cli-driving

Here's the next step:

	Task 0: DONE? YES!
		Cop cars don't go into chase mode until the player is X distance in front of them and Y cone radius in front of them. (Exposed editor properties)
	
		
Notes:

Clarification Questions and Answers:

    Q1 (Task 0): Which "chase mode" gets the X distance / Y cone gate? (Options: the sight handoff, where the car switches to AICarDriver, rams the player and turns its lights on / the Pac-Man Chase phase of the Scatter/Chase schedule / both.)
    A1: The sight handoff (AICarDriver ram + lights on).

    Q2 (Task 0): What unit is Y, the cone size? (Options: half-angle in degrees off the car's nose / full cone width in degrees / radius in world units at distance X.)
    A2: Half-angle in degrees off the car's nose (Y=30 means a 60 degree wide cone).

    Q3 (Task 0): Once a car is chasing, does the X/Y gate also end the chase? (Options: entry only, the chase still ends after losing line of sight for LoseSightGraceSeconds / both ways / separate wider exit properties.)
    A3: Entry only: the chase still ends only after losing line of sight for LoseSightGraceSeconds (today's exit rule).

    Q4 (Task 0): Which player can trigger the chase? (Options: any player inside the cone and range with line of sight, the car chases the nearest of those / only the car's current Pac-Man target, the nearest player overall.)
    A4: Any player inside the cone and range with line of sight; the car chases the nearest of those.

    Q5 (Task 0): Also draw the cone and range as a debug gizmo? (Options: no, editor properties only / yes, when selected / yes, behind a DrawDebug toggle.)
    A5: No, editor properties only.

    Parent decisions (change if wrong):
    - X and Y are two new GhostCarBrain properties, ChaseStartDistance (default 1500) and ChaseStartConeHalfAngle (default 30, range 0 to 180, 180 = no cone). The existing SightRange (3000) stays the range for keeping a chase, so a chase starts within the smaller of X and SightRange.
    - Distance and angle are measured flat (top-down), from the car's nose direction, the same way SightRange is measured today. A chase also still needs a clear sight ray.
    - While chasing, the car stays on the player it started chasing. A nearer player does not steal the chase. The chase ends when sight of that player is lost for LoseSightGraceSeconds, or right away when that player is gone or disabled (bar, minigame, end screen). The Pac-Man targeting (nearest player) keeps driving the navmesh marker as before.
    - Chases can still start during Scatter, as today.
    - Not part of the Task 0 commit and left untouched: the uncommitted minimal.scene edit (water_bottom lowered 40 units, "water" tag added to the water object) and the untracked CURRENT_GOAL.md.bak.


IGNORE: <</goal Update sboxgamejam3 project state according to document CURRENT_GOAL.md. If "SESSION START?" is "NO!" then consider best clarifying questions, ask them, and record them below and after that update "SESSION START?" to "YES!" >>
