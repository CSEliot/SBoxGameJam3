# Summary

1. A multiplayer s&box game, the game is a mix of "Pepsiman" and "Crazy Taxi".
2. Each player's character will start in a pub, sitting at a bar, which is the game hub / lobby.
3. When starting the game the character will first play a drinking minigame before burst out running from the pub.
4. The player's character will always be running in a city, and it can't die when colliding with obstacles.
5. Colliding with object will drop your pants, which will slow you and make your jump pathetic.
6. Smashing the "R" key will help pulling the pants up, which will allow the character to move faster and jump.
7. An arrow indicator will point to a random bar location, the 3 nearest bars will not be included in the random selection.
8. The arrow indicator will always check for the closest bar, so it's dynamic.
9. The bar that gets visited will get locked.
10. After visiting all bars it will unlock all the bars again. (Note: remove the rule for excluding the nearest 3 bars when the total bars available is 4)
11. Drinking increase your drunkenness meter, drunkenness level go down via time passing.
12. Every bar visit increases the drunkenness floor slightly, increasing game difficulty over time.
12. Higher drunkenness level unlocks a new unhinged area with a crazy theme.
13. When in a bar you will play a simple mini-game of smashing the space bar, the better you perform the higher the drunkenness meter will get.
14. When reaching a bar you have a choice of stop drinking or "go for one more round".
15. If you sober up the game will end, and you will lose all your points.
16. A leaderboard will show the best drunkard.

# North Star:

Combining two classic games (Pepsiman and tic-tac-toe) to create a competitive chaotic fast paced game.
You will have to get really drunk to unlock crazy areas to access unique bars for better score.
Every second you're playing the game, you get a point. The higher your drunkenness level, the more points per second.
The more drunk you are the harder for you to control the character.
To add to the chaos you will be competing with other players at the same time.

# Key Feeling:

It's pure carnage and chaos filled with liquor and non-stop running, aim to be the best drunkard to unlock unhinged areas with premium rewards, aim for the top and don't sober up.

# Section 1 — Essential Core Mechanics (the moment-to-moment loop):

# Section 2 — Meta-Features, the Numbers, and Lifecycle Systems (the systems that make the loop meaningful):

## Details

### Definitions


Pub: It's the starting player location, they serve as the game hub / lobby, there are multiple starting location.
-- note SYNC these terms.
Bar: It's a location that host a drinking mini-game, that will increase your drunkenness level.
The bar will give the player two options:

1. Stop drinking: Which will allow the player to cash-in his score.
2. Continue drinking: AKA "Go for one more round" will allow the player to continue playing in hopes for a higher score.

Drunkenness-Meter: An indicator that show how drunk are you in beers. 1.5 beers drunk, 10 beers drunk, 3.75 beers, etc.

Un-lockable Area: A special area that unlock on higher drunkenness level.

Sober-up: Is a game mechanic were the drunkenness level goes down the more time passes.

Game Over: The game has two endings:

1. You stop drinking to cash-in your current score.
2. Your drunkenness level reaches 0 which causes players score to be lost. No leaderboard submission.

Hit: Anything that causes player to go into KnockedDown state. Ex: Tilting too much left or right is a 'hit'. Colliding is a 'hit'. Running into wall is a 'hit'.

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
- the pants are either fully up or fully down
- fixed camera
- sober mechanic every x second we lose x number of beers
- buzz effect protect players from losing sober for the first 20 second after leaving a bar
- ankle wizard bat

* priority list:
  V 0.0

- drunken character controller
- a one street with 2 bars at both ends
- basic obstacles
- drinking mini game
- sober overtime

