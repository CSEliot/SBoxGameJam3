// Project: sboxgamejam3
// File:    BeerLevelCap.md
// Author:  cseliot (planned with Hermes Agent)
// Created: 2026.09.23
//
// Copyright (c) 2026 KiteLion Games, LLC. All rights reserved.
//
// Description:
// Plan + inventory for GameManager.BeerDifficultyCap: after X beers, the DRIVENESS-SCALED
// DIFFICULTY effects stop getting worse. Score, HUD readouts, and every non-difficulty
// use of BeerLevel keep scaling with the raw level.

# Beer Difficulty Cap - Plan

## Goal

Add `GameManager.BeerDifficultyCap`. Beyond this many beers, the values that BeerLevel
drives which make the game HARDER saturate (stop increasing). Everything BeerLevel drives
that is not a difficulty ramp (score multiplier, HUD/EndGame readouts) keeps using the
raw level.

`BeerLevel` itself is NOT clamped - it still grows without limit ("No maximum" stays true).
The cap is applied at each difficulty READ site via `min(BeerLevel, cap)`.

## Complete inventory: everything BeerLevel impacts (verified on disk, 2026-09-23)

Grep basis: every read of `BeerLevel` under `Code/`.

### A. Difficulty effects - CAPPED by this feature
(line numbers below are post-implementation positions, 2026-09-23 02:20; they shifted once
already due to a parallel rename pass in DrunkCC.cs)

| # | Site | What it does | Why it's difficulty |
|---|------|--------------|---------------------|
| 1 | `DrunkCC.cs:383` (`HandleRunningHelper`) | Forward drive force multiplier `( _BeerToSpeedMultiplier + 1 ) * ( DifficultyBeerHelper() + 1 )` | Higher drunkenness = faster speed (Design.md core rule) = harder to steer/avoid |
| 2 | `DrunkCC.cs:403` (`HandleRunningHelper`) | Lean tap impulse `_LeanImpulse * ( 1 + DifficultyBeerHelper() * _DrunkLeanScale )` | Each Left/Right tap rolls you harder = harder control, more knockdowns (Design "dangerously strong" at 10+) |
| 3 | `DrunkCC.cs:458` (`EnterKnockedDownHelper`) | Knockdown recovery rewind `rewindSeconds = DifficultyBeerHelper() * _SecondsPerBeer` | Drunk = thrown further back along your trail after a ragdoll = bigger setback |
| 4 | `DrunkCC.cs:659` (`RecordRunningHistoryHelper`) | History buffer window `DifficultyBeerHelper() * _SecondsPerBeer + _RecoveryHistoryHeadroom` | Bookkeeping for #3; capped in lockstep so the trail is never shorter than the capped rewind nor uselessly longer |

### B. Camera jerk - flagged, NOT wired to BeerLevel today

`CCCamera.cs:719` computes `jerkSpeedBeerified = _JerkSpeed * _ScaleJerkSpeedToBeerLevel`.
Despite the property name (`CCCamera.cs:44`, `[Property, MinMax(1,99)]`), `_ScaleJerkSpeedToBeerLevel`
is a plain hand-tuned INSPECTOR CONSTANT. Nothing in `CCCamera.cs` (or its full git history -
checked with `git log -S BeerLevel` on the file) ever reads `DrunkCC.BeerLevel`.
`_drunkCC` is resolved there only for the `CurrentState == Running` behind-cap gate
(`CCCamera.cs:259`).

The task's parenthetical "(assuming that the camera jerk by beer level still happens)" does
NOT hold: the camera jerk happens every Left/Right press, but it is not modulated by beer
level at all - not capped, not uncapped. There is nothing to cap. Consequences:

- This feature changes nothing in `CCCamera.cs`. The jerk behaves exactly as it does today.
- The misleading property NAME is left alone (renaming a `[Property]` orphans the value
  already tuned in `minimal.scene` - see the project's rename hazard). It is reported here
  instead, not silently fixed.
- If actual jerk-scales-with-beer behavior is ever wanted, the cap plugs in the same way:
  multiply by `min( BeerLevel, BeerDifficultyCap )` at that read site. Offered as a
  follow-up, out of scope now.

### C. Non-difficulty effects - NOT capped (raw level keeps scaling)

| # | Site | What it does | Why left alone |
|---|------|--------------|----------------|
| 5 | `PlayerProgress.cs:137` | Score-per-second multiplier `1 + BeerLevel * DrunkScoreScale` | This is the REWARD side of drinking, not a difficulty ramp; capping it would punish players for the thing the cap exists to protect. Still reads raw `BeerLevel` (verified post-change). |
| 6 | `Hud.razor.cs:125` (`BeersText`) | HUD "beers" readout, formatted raw | Display of factual beer count |
| 7 | `EndGame.razor:200` (`BeersText`) | End screen "Beers Drunk" readout, formatted raw | Same |
| 8 | `GameManager.cs:304` (`EndMiniGameHelper`) | WRITE site: `+= _minigameController.Beers` after each round | A write, not a difficulty read; stays raw so 5-7 keep growing |
| 9 | `GameManager.cs:514` (`HandleEndGameTryAgain`) | WRITE site: Try Again resets to 0 | Stays raw |

### D. Design-doc items that do not exist in code (no cap needed/possible)

- Design.md "Drunk Effect System" (wobble/blur/chromatic aberration/UI tilt tiers at
  0-3 / 3-6 / 6-10 / 10+): NOT implemented anywhere in `Code/UI/` (verified by grep - the
  HUD only prints the number). Nothing to cap.
- "Unhinged bonus areas" unlocked by drunkenness: unimplemented.
- Pants states / "RECOVERY NEEDS TO TAKE INTO ACCOUNT NEW BEER LEVELS" (TODO.MD line 3):
  the pants system doesn't read BeerLevel today; #3/#4 above are the live recovery scaling.
- Emergency Beer / hourglass pickups: unimplemented, and timer-time is not a difficulty read.

## Design of the cap

- Location (as requested): `GameManager`.
  `[Property] public float BeerDifficultyCap { get; set; } = 10f;`
  - `float` to match `BeerLevel`'s type (partial beers accumulate via `Minigame.Beers`).
  - Default 10: matches Design.md's "10+" top difficulty tier - past tier-4 saturation the
    cap says "the game stops getting harder". Inspector-tunable per scene.
  - Convention (mirrors `_MaxHitRoll`'s existing "0 disables" idiom in this codebase):
    `BeerDifficultyCap <= 0` = NO cap, difficulty keeps scaling forever (old behavior).
- Mechanism: the raw `BeerLevel` stays untouched; each difficulty READ SITE computes
  `min( BeerLevel, cap )` through one tiny helper in `DrunkCC`:
  `private float DifficultyBeerHelper()` (implemented; `Helper()` suffix per project convention).
  - Why not clamp at the write site (`GameManager.cs:295`): would break #5-#7 (score/HUD
    must show real beer count).
  - Why pull (helper reading `BeerDifficultyCap` from the resolved GameManager) instead of
    push (GameManager writing a second field on every level change): two write sites exist
    today, more may come; a pull can't miss one. Cap value itself is a scene-property,
    identical on all clients for the local player's own simulated DrunkCC (physics only
    runs owner-side behind the existing `IsProxy` gate in `HandleRunningHelper`).
  - `DrunkCC` resolves the scene `GameManager` lazily inside the helper
    (`_gameManager ??= Scene.GetAllComponents<GameManager>().FirstOrDefault()`), so the cap
    works even before the user's `OnSpawnHelper` RPC lands and in solo test scenes. If still
    absent it falls back to uncapped, i.e. current behavior.
- Files touched:
  1. `Code/GameManager.cs` - add `BeerDifficultyCap` property (with doc comment).
  2. `Code/DrunkCC.cs` - add `_gameManager` field + `DifficultyBeerHelper()`; swap the four
     difficulty reads (#1-#4) to use it; update the now-stale "no cap" comments at line
     378-379 and the `BeerLevel` doc at line 33-36.
  3. `Code/CCCamera.cs` - NO change (section B).
- Tunables already serialized in `minimal.scene` are untouched: no existing `[Property]` is
  renamed or re-meaned, so no inspector re-tuning is needed anywhere. Only the brand-new
  `BeerDifficultyCap` starts at its C# default (10) until set in the inspector.

## Verification plan

1. Offline compile gate (`CodeTestPortable`): `dotnet build GameCompileCheck.csproj -c Release`
   (no `.razor` changed -> razorgen skip is fine; gen/ already current).
   Proves binding only.
2. User playtest checklist (in-editor, since physics feel is his loop):
   - Set cap low (e.g. 3). Drink past it. Speed and tap-roll strength stop growing; raw
     beer number and score multiplier keep growing.
   - Knockdown past the cap rewinds ~3 beers' worth, not more.
   - Cap 0 => old behavior (no saturation) at any level.
   - Camera jerk feels identical to today at all levels.

## As implemented (2026-09-23)

- Status: DONE and compile-gated. `dotnet build GameCompileCheck.csproj -c Release` ->
  0 Errors (one pre-existing CS0414 warning on `_hasCheckedHistObstacle`, unrelated - it
  predates this change and belongs to the in-flight DrunkCC refactor).
- Final diff of THIS feature (verified via grep after build):
  - `GameManager.cs`: new `[Property] public float BeerDifficultyCap { get; set; } = 10f`
    (line 81) with doc comment. Nothing else in GameManager touched by this feature; the
    `OnSpawn(Connection)` stub now at its end and the DressPlayerHelper move into DrunkCC
    are the user's own concurrent edits, left as found.
  - `DrunkCC.cs`: `_gameManager` field + `DifficultyBeerHelper()` (lazy resolve); the four
    difficulty reads (speed multiplier, lean impulse, rewind seconds, history window) now
    call it; `BeerLevel` doc + two stale "no cap" comments updated. Raw `BeerLevel`
    remains the source for score (PlayerProgress) and all UI readouts.
  - `CCCamera.cs`: untouched, as planned (jerk is not beer-driven; see section B).
- Note: default cap is 10 in C#. `minimal.scene` was resaved after the property landed and
  now SERIALIZES `"BeerDifficultyCap": 10` (line 236), so the scene pins the value: if the
  C# default ever changes, the scene's 10 silently wins until re-tuned in the inspector.
  Set it to 0 there for old no-cap behavior.
- Cap semantics vs "after X beers": the cap counts `BeerLevel`, which a fresh player prefab
  starts at 1 (not 0), so a cap of N saturates at N-1 beers-won for a brand-new run (a Try
  Again reset goes to raw 0). Pre-existing `BeerLevel = 1` default, inherited knowingly;
  don't "fix" it by lowering cap values - account for the +1 when tuning.
