# Architecture.md

# Play By Play - v0

Minigame Starts.

Local Game State:
InBar = true
MiniGameStarted = true

GameManager tracks and controls renderings of character in pub. Players are allowed to make networked calls, but host is owner of data.

barslots

playerID = locally tracked ID by GameManager

X Player prefabs exist in a line. When EnterBar(playerID) (a global call amongst all clients) is called, the next available Drinker gameobject is enabled, their BarCam is enabled IF LOCAL, clothing set to playing players' clothing, and
when player plays minigame, we spawn Beer at only that BeerSpawnLocation of THAT Drinker/BarCam/BeerSpawnLocation.




Every Minute, the starting seed that is passed to connecting players is changed.
