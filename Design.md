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
