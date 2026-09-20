# Summary

1. A multiplayer s&box game, the game is a mix of "Pepsiman" and "Crazy Taxi".
2. Each player's character will start in a bar, sitting at a bar, which is the game hub / lobby.
3. When starting the game the character will first play a drinking mini-game before burst out running from the bar, you will have 2 minute till you reach the next bar.
4. The player's character will always be running in a city, and it can't die when colliding with obstacles.
5. Colliding with object will drop your pants, which will slow you and make your jump pathetic.
6. Smashing the "R" key will help pulling the pants up, which will allow the character to move faster and jump.
7. Colliding with an obstacle while your pants at their lowest will make you trip over, when getting up you will lose all the momentum you had, so you will have to build it back up again.
8. An arrow indicator will point to a random bar location, the 3 nearest bars will not be included in the random selection.
9. The arrow indicator will always check for the closest bar, so it's dynamic.
10. The bar that gets visited will get locked.
11. After visiting all bars it will unlock all the bars again. (Note: remove the rule for excluding the nearest 3 bars when the total bars available is 4 or less)
12. Every bar visit increase your drunkenness meter, and increase your timer by 1 min .
13. The higher drunkenness meter is the faster you can run and the harder you tilt.
14. Higher drunkenness level unlocks a new unhinged area with a crazy theme.
15. When in a bar you will play a simple mini-game of smashing the space bar, the better you perform the higher the drunkenness meter will get and more time you will get.
16. When reaching a bar you have a choice of stop drinking or "go for one more round".
17. If you run out of time before reaching the next bar, the game will end and you will lose your points.
18. A leaderboard will show the best drunkard.

# North Star:

Combining two classic games (Pepsiman and tic-tac-toe) to create a competitive chaotic fast paced game.
You will have to get really drunk to unlock crazy areas to access unique bars for better score.
Every second you're playing the game, you get a point. The higher your drunkenness level, the more points per second.
The more drunk you are the harder for you to control the character.
To add to the chaos you will be competing with other players at the same time.

# Key Feeling:

It's pure carnage and chaos filled with liquor and non-stop running, aim to be the best drunkard to unlock unhinged areas with premium rewards, aim for the top or risk it all.

# Section 1 — Essential Core Mechanics (the moment-to-moment loop):

1. You start at the bar, there is multiple starting bars.
2. First time you leave the bar you will have 2 mins to reach to the next bar.
3. An arrow indicator will show the closest available (unvisited) bar, excluding the 3 closest bars when the total available bars are more than 4 bars.
4. When the total available (unvisited) bar are 4 or less, the arrow indicator will point to the closest available (unvisited) bar without any restrictions.
5. When reaching the bar you will be prompted with two options:
   a. Cash-in your score: end your game.
   b. Play mini-game: continue playing.
6. Every bar visit have a mini-game, it's a simple game of pushing space bar key.
7. The better your perform in the mini-game, the higher your Timer/ Drunkenness level will increase.
8. Note: When entering the bar your timer will be paused.
9. Note: When existing the bar the new time that you won will be added to your timer, and the timer will resume.
10. The city will have a "hour glass" loot object that give players more time to add to their timer (the object can be a simple beer bottle with +3 sec on it).
11. Players can't compete for the same hour glass but only one player can take it, the server will handle hour glass generation.
12. The character will be always running, the character can't die.
13. When running into an obstacle the character's pants will drop one level.
14. The pants have three levels (fully-up, to the knees, fully-down to the ankle).
15. Pants level effect speed and jump power.
16. If you hit an obstacle while your pants are fully down then you will be tripped over, which will make you lose your current speed and lose 1-2 seconds till you get up.
17. After tripping down, the character will automatically get up, the character will have to slowly build momentum to reach the top speed.
18. A rescue button will be added to free players if getting stuck, using the button will make you lose all your speed and will relocate you to a close location from which the direction you came from.
19. If your timer hits zero, you will lose all the score that you have built unless you have emergency beer bottle item on you.
20. Emergency beer bottle is an item you can get in the map, you can carry only one at a time, it will be used automatically when the timer hits zero, it's to give players one more chance in the game.

# Section 2 — Meta-Features, the Numbers, and Lifecycle Systems (the systems that make the loop meaningful):

## Details

### Definitions

Bar: It's the starting location / checkpoint.
The bar will give the player two options:

1. Stop drinking: Which will allow the player to cash-in his score.
2. Continue drinking: AKA "Go for one more round" will allow the player to continue playing in hopes for a higher score.

The bar will effect two things, which are effected by how well you perform in the mini-game:

1. Drunkenness level
2. Time

Drunkenness-Meter: An indicator that show how drunk are you in beers. 1.5 beers drunk, 10 beers drunk, 3.75 beers, etc.

Un-lockable Area: A special area that unlock on higher drunkenness level.

Game Over: The game has two endings:

1. You stop drinking to cash-in your current score.
2. Your run out of time and lose all your score.

Hit: Anything that causes player to go into KnockedDown state. Ex: Tilting too much left or right is a 'hit'. Colliding is a 'hit'. Running into wall is a 'hit'.

Jerk: When the camera moves in response to movement input.

# Section 3 - Visuals

This is going to occur at dusk and quickly become night time. In a city, where windows glow behind semi-lit city structures (using shaders) and street lamps.

Here's our color palette, use this for GUI design and other visuals:
#00166D #4C74E5 #FFD51B #FF6120 #E03AC0

Game uses a cartoonish pixar-esque mid-low poly (higher than mobile game "low poly") art style for the 3D Models.

Otherwise game depends on the realistic-ish Citizen Terry Sausage characters built in to the platform.

# Section 4 - UI Design

## Design System

### Color Palette
- **Background**: #00166D (deep blue, dusk sky)
- **Primary glow**: #4C74E5 (medium blue, default neon edges)
- **Accent warm**: #FFD51B (yellow, timer, score)
- **Accent hot**: #FF6120 (orange, danger states, pants warning)
- **Accent wild**: #E03AC0 (magenta, drunkenness, game over)

### Typography
- **Display** (score, timer): Monospace or chunky sans-serif, large, with glow
- **Body** (labels, prompts): Clean sans-serif, medium weight
- All text should have subtle outer glow matching its color

### Drunk Effect System
As drunkenness meter increases (measured in beers, 0 to unbounded):
- **0-3 beers**: Clean UI, subtle glow only
- **3-6 beers**: Slight wobble on text (1-2px oscillation), mild blur on edges
- **6-10 beers**: Strong wobble (3-5px), chromatic aberration on text (RGB split), occasional double-vision flicker
- **10+ beers**: Heavy blur, strong RGB split, UI elements tilt with player lean, color shift toward magenta

Effects apply to: timer, score, drunkenness meter display, pants indicator. Arrow indicator stays crisp (gameplay critical).

---

## Screen 1: HUD (Always Visible During Gameplay)

### Layout
```
[TOP-LEFT]                    [TOP-CENTER]           [TOP-RIGHT]
Timer                         Score                  Drunkenness
[2:00]                        [0042]                 [🍺 3.5]

[BOTTOM-CENTER]
Arrow Indicator (3D, floating above ground, points to target bar)

[BOTTOM-LEFT]
Pants Level
[▮▮▮] (3 bars, filled = current level)
```

### Elements

**Timer (top-left)**
- Format: `M:SS` (e.g., `2:00`)
- Color: #FFD51B (yellow), shifts to #FF6120 (orange) when <30s, flashes when <10s
- Size: Large (80px display font)
- Behavior: Counts down, pauses in bar menu
- Drunk effect: Wobble + blur at high levels, double-vision flicker at 10+ beers

**Score (top-center)**
- Format: 4-digit zero-padded (e.g., `0042`)
- Color: #FFD51B (yellow)
- Size: Large (80px display font)
- Behavior: Increments every second, multiplier increases with drunkenness
- Drunk effect: Same as timer

**Drunkenness Meter (top-right)**
- Format: `🍺 X.X` (beer emoji + decimal beers)
- Color: #E03AC0 (magenta)
- Size: Medium (60px display font)
- Behavior: Increases after bar visits and mini-games, no cap
- Drunk effect: This element itself wobbles/blurs, making it hard to read at high levels (intentional chaos)

**Arrow Indicator (bottom-center, 3D world-space)**
- Format: 3D arrow model floating above ground, points toward target bar
- Color: #4C74E5 (blue) with #FFD51B (yellow) glow
- Size: Medium (visible but not obstructive)
- Behavior: Dynamically updates to point to closest valid bar, rotates smoothly
- Drunk effect: **None** (gameplay critical, must stay readable)

**Pants Level (bottom-left)**
- Format: 3 vertical bars (like battery indicator)
- Color: #4C74E5 (blue) when full, #FF6120 (orange) when 1 bar, flashes red when 0 bars (trip risk)
- Size: Small (40px tall)
- Behavior: Decreases on obstacle hit, increases when player mashes R
- Drunk effect: Slight wobble at high levels

---

## Screen 2: Bar Menu (Modal Overlay)

### Layout
```
[CENTER OF SCREEN]
┌─────────────────────────────────────┐
│         🍻 BAR NAME 🍻              │
│                                     │
│  [CASH IN SCORE]                    │
│  End run, save 0042 points          │
│                                     │
│  [PLAY MINI-GAME]                   │
│  Drink more, get drunker, get time  │
└─────────────────────────────────────┘
```

### Elements

**Modal Background**
- Semi-transparent dark overlay (#00166D at 80% opacity)
- Blurred backdrop of game world

**Modal Container**
- Border: 2px solid #4C74E5 (blue) with glow
- Background: #00166D (deep blue) at 90% opacity
- Padding: 40px

**Title**
- Text: Bar name (e.g., "THE DRUNKEN SAILOR")
- Color: #FFD51B (yellow)
- Size: 60px display font
- Drunk effect: Wobble applies

**Button: Cash In Score**
- Text: `CASH IN SCORE` + subtitle `End run, save [score] points`
- Background: Transparent with #4C74E5 border
- Hover: Fill with #4C74E5, text glows #FFD51B
- Size: 400px wide, 80px tall
- Drunk effect: Button wobbles slightly

**Button: Play Mini-Game**
- Text: `PLAY MINI-GAME` + subtitle `Drink more, get drunker, get time`
- Background: Transparent with #E03AC0 border
- Hover: Fill with #E03AC0, text glows #FFD51B
- Size: 400px wide, 80px tall
- Drunk effect: Button wobbles more than cash-in (temptation visual)

**Timer Display**
- Paused timer shown in corner of modal
- Color: #FFD51B (yellow), dimmed
- Size: 40px
- Behavior: Frozen while menu is open

---

## Screen 3: Mini-Game UI (Spacebar Mashing)

### Layout
```
[TOP-CENTER]
┌─────────────────────────────────────┐
│         CHUG! CHUG! CHUG!           │
│                                     │
│  [████████████████░░░░░░] 85%       │
│                                     │
│  SMASH SPACEBAR!                    │
│  [TIME: 5s]                         │
└─────────────────────────────────────┘
```

### Elements

**Title**
- Text: `CHUG! CHUG! CHUG!`
- Color: #E03AC0 (magenta)
- Size: 80px display font
- Behavior: Pulses/bounces with each spacebar press
- Drunk effect: Heavy wobble, RGB split

**Progress Bar**
- Format: Horizontal bar, 600px wide, 40px tall
- Fill color: Gradient from #4C74E5 (blue) to #E03AC0 (magenta) as it fills
- Border: 2px solid #4C74E5 with glow
- Behavior: Fills with each spacebar press, decays slowly over time
- Drunk effect: Bar itself wobbles, making it hard to judge fill level

**Percentage**
- Text: `XX%` next to progress bar
- Color: #FFD51B (yellow)
- Size: 60px display font
- Behavior: Updates in real-time

**Instruction**
- Text: `SMASH SPACEBAR!`
- Color: #FF6120 (orange)
- Size: 60px display font
- Behavior: Flashes/pulses

**Timer**
- Text: `TIME: Xs` (countdown from 5-10 seconds)
- Color: #FFD51B (yellow), shifts to #FF6120 when <3s
- Size: 60px display font
- Behavior: Counts down, mini-game ends at 0

**Visual Feedback**
- Screen shake on each press
- Beer splashes/particles on high combo
- Character model in background performs drinking animation

---

## Screen 4: End-Game Screen (Game Over / Cashed In)

Triggered by either losing all score to a zeroed timer (Game Over) or a voluntary
Cash In at a bar (You Cashed In). Unlike the old design, the leaderboard is NOT
behind a button here - the Top 10 renders inline on this screen, with the local
player's own score directly beneath it, followed by the exit buttons. This screen
owns the same leaderboard row spec as Screen 5 below (Screen 5 is the reusable
component definition; this is its embedded, always-expanded use).

### Layout
```
[CENTER OF SCREEN]
┌─────────────────────────────────────┐
│      GAME OVER / YOU CASHED IN      │
│                                     │
│         🏆 BEST DRUNKARDS 🏆        │
│  1. PlayerName1    0987   12.5🍺    │
│  2. PlayerName2    0742   10.0🍺    │
│  3. PlayerName3    0521    8.5🍺    │
│  ...                                │
│  10. PlayerName10   0102   3.0🍺    │
│                                     │
│  YOUR SCORE: 0042                   │
│      (or: 0̶0̶0̶0̶ w/ "WASTED" stamp)  │
│  Beers Drunk: 7.5 🍺               │
│  Bars Visited: 3                    │
│                                     │
│  [TRY AGAIN]                        │
│  [QUIT]                             │
└─────────────────────────────────────┘
```

### Elements

**Title**
- Text: `GAME OVER` (if timer ran out) or `YOU CASHED IN` (if player chose to stop)
- Color: #FF6120 (orange) for game over, #FFD51B (yellow) for cash-in
- Size: 100px display font
- Behavior: Fades in with glow
- Drunk effect: None (game over, player is sober now)

**Leaderboard (Top 10, embedded)**
- Same row spec as Screen 5's Leaderboard Rows (format, rank colors, current-player
  highlight), but always Top 10 and always visible on this screen - no CLOSE button,
  no open/close state, it just renders as part of the End-Game layout.
- If the local player is outside the Top 10, their row still appears (highlighted)
  pinned below rank 10, separated by a thin #4C74E5 divider, so they can always see
  their own placement even off the visible leaderboard.
- Sits directly beneath the Title and above Your Score.

**Your Score**
- Text: `YOUR SCORE: 0042`, 4-digit, #FFD51B (yellow), 70px display font
- Sits directly beneath the embedded leaderboard, above Beers Drunk / Bars Visited
- **Zero-score treatment ("WASTED")**: if Final Score == 0, the `0000` digits get a
  strikethrough (line through the text, matching text color) and the word `WASTED`
  is stamped on top of/over the score, rotated crooked (roughly -12 to -18 degrees,
  vary per instance so it doesn't look too neat), in the display font, large enough
  to overlap the crossed-out score. Color: #FF6120 (orange) - reads as a rubber-stamp
  "you get nothing" gag, not a somber failure state. This only triggers on an EXACT
  zero score, not just "low" scores.

**Stats**
- Beers Drunk: Decimal + beer emoji, #E03AC0 (magenta)
- Bars Visited: Integer, #4C74E5 (blue)
- Size: 60px display font each
- Layout: Vertical stack, left-aligned, beneath Your Score

**Button: Try Again**
- Text: `TRY AGAIN`
- Background: Fill #E03AC0 (magenta)
- Hover: Brighter glow

**Button: Quit**
- Text: `QUIT`
- Background: Transparent, #FF6120 border
- Hover: Fill #FF6120

---

## Screen 5: Leaderboard (Row Spec / Standalone Overlay)

Defines the leaderboard row rendering used by Screen 4's embedded Top 10. Whether
this ALSO still exists as an independently-opened overlay elsewhere (e.g. from the
ESC menu, mid-run) is an open question now that the End-Game screen embeds it
directly - not decided here, flagging rather than assuming either way.

### Layout
```
[CENTER OF SCREEN]
┌─────────────────────────────────────┐
│         🏆 BEST DRUNKARDS 🏆        │
│                                     │
│  1. PlayerName1     0987   12.5🍺   │
│  2. PlayerName2     0742   10.0🍺   │
│  3. PlayerName3     0521    8.5🍺   │
│  4. PlayerName4     0421    7.0🍺   │
│  5. PlayerName5     0312    6.5🍺   │
│  6. PlayerName6     0288    6.0🍺   │
│  7. PlayerName7     0255    5.5🍺   │
│  8. PlayerName8     0201    5.0🍺   │
│  9. PlayerName9     0178    4.5🍺   │
│ 10. PlayerName10    0102    3.0🍺   │
│                                     │
│  [CLOSE]                            │
└─────────────────────────────────────┘
```
(`[CLOSE]` only applies if used as a standalone overlay - not present in Screen 4's
embedded use.)

### Elements

**Title**
- Text: `🏆 BEST DRUNKARDS 🏆`
- Color: #FFD51B (yellow)
- Size: 80px display font
- Trophy emoji glows

**Leaderboard Rows**
- Format: `Rank. Name    Score    Beers`
- Shows Top 10 (was Top 5 in an earlier draft - now Top 10 per current spec)
- Rank 1: #FFD51B (gold/yellow)
- Rank 2: #4C74E5 (silver/blue)
- Rank 3: #FF6120 (bronze/orange)
- Rank 4+: White/dim
- Size: 40px sans-serif
- Layout: Monospace alignment for numbers
- Current player's row highlighted with #E03AC0 glow (see Screen 4: if the local
  player isn't in the Top 10, their row is pinned below it instead of omitted)

**Close Button** (standalone overlay use only)
- Text: `CLOSE` or `X` in corner
- Standard button style

# Section 5 - Character Controller and Camera Implementation
The intent of this character controller is to recreate the feeling of driving a motorcycle but it's a human cc.
Unlike traditional cc design, this cc uses and relies on forces instead of "setting velocity". A very physics-tied cc.

Players only press left and right to both:
 1. Move left and right (like a motorcycle)
 2. Adjust and "fix" the LEAN of the character.

This cc has gameplay mechanic rules to follow:
 1. When the cc has a drunkness level at 0, the character stays nearly completely up straight as they run and gain speed.
 2. Drunkenness level has no maximum. But at 10+ level Drunkenness, the character dangerously strongly rolls into the direction input by the player. We intend the game to be VERY >
 3. The cc both yaws and rolls into the turn.
 4. At medium level Drunkenness, it is expected that the player, if turning right, will lightly throttle the "Right" key, so to turn but not so hard they fall over.  The Drunkenne>
 5. At roll degree X (positive or negative), the cc enters the "knockeddown" state due to having leaned over too much.
 6. knockeddown state == Rag-dolling for X amount of time, before being reset.
 7. Upon resetting, the cc starts again at 0 velocity and must gain momentum again.

---

## Implementation Notes

### Drunk Effect Performance
- Wobble: Use shader or UI animation, sine wave on position/rotation
- Blur: Gaussian blur shader pass on UI layer
- RGB split: Chromatic aberration shader (offset R, G, B channels)
- Double-vision: Render UI twice with offset, blend
- All effects should scale smoothly with drunkenness value, not step

### Accessibility
- Arrow indicator never gets drunk effects (gameplay critical)
- Timer remains readable even at high drunk levels (reduce blur, keep wobble)
- Consider colorblind mode: use shapes/symbols alongside colors for pants level

### s&box Specifics
- UI likely built with s&box UI system (HTML/CSS or native panels)
- 3D arrow indicator is a world-space model, not screen-space
- Drunk effects may require post-processing shader on UI render target
- Test performance with all effects active at max drunk level

### Networking

### Session-Scope

### Per-MiniGame-Scope

### Immediate Future

### Future

### Notes

- the character is always running, and it can't die
- wall collision
- the camera is fixed
- arrow indicator that points to the closest unvisited bar
- when hitting obstacles your pants will go down, when the pants are all the way down you will have a pathetic jump and slower speed
- to pull your pants up you need to smash the R button, when the pants are up you can jump better and be faster when running
- the pants are either fully up or fully down or to the knees
- fixed camera
- buzz effect protect players from losing sober for the first 20 second after leaving a bar
- ankle wizard bat

* priority list:
  V 0.0

- drunken character controller
- a one street with 2 bars at both ends
- basic obstacles
- drinking mini game
- sober overtime
