5. Minigame.SpawnABeerHelper (Minigame.Logic.cs:119) — unbounded object leak, wrong position remotely.
Broadcast on every spacebar press, clones _Mug locally on every client, nothing ever destroys them. At ~10 presses/s × players × rounds that's thousands of GameObjects per session. And it clones at the receiver's BeerSpawnLocation, which (given bug 1) is set only on the host and reflects whoever sat down last — so remote beers appear at the wrong seat. Pass the position as an RPC argument and give the mug a lifetime.

6. Traffic you're paying for and not using (your stated "only game-necessary" bar):
- GameManager._SecondsUptime is [Sync], rewritten every frame on the host, and read only by the host. Drop [Sync].
- GameManager._Bars is [Sync] Bar[]. Arrays are IList, so NetworkTable.Entry.Init sets NeedsQuery = true (NetworkTable.cs:63) — the engine re-serializes and diffs all 10 component refs every network tick, for a value that is scene-authored, identical on every client, and never assigned at runtime.
- InitialClientSetupHelper is [Rpc.Broadcast] whose body is if (connection == Connection.Local). It's a broadcast with exactly one real recipient — use Rpc.FilterInclude(connection).
- DressPlayerHelper hand-rolls the active dressing case for a body the connection already owns. The player prefab's Dresser.Source is Manual; setting it to OwnerConnection removes the RPC entirely and avoids RemoveUnownedItems silently no-op'ing for remote players on non-host clients.

8. Smaller, still real:
- GameManager._SpawnPlayerHelperCalled (line 116) is declared and read (458) but never assigned. Dead guard. With _ImmediatelySpawnRunner = true you'd get two player clones, one of them orphaned and unowned.
- _localGameState initializes to WaitingToStartMinigame (line 130), so on frame 1 every client broadcasts SitDownPlayer for bar 0 before entering any trigger. Minigame._StartOnPlay is also still true in the saved scene.
- Bar.OnTriggerEnter/Exit has no tag filter on other — ragdoll bone colliders resolve to the player network root, so Network.IsOwner is true for them and the event fires repeatedly.
- CCCamera.TryResolveGetDrunkHelper returns _Target.IsValid() instead of _drunkCC.IsValid() (line 426).
- UpdateNextBarData uses _localRandom and is called at each client's own minigame end; the "deterministic per client" comment at GameManager.cs:394 is wrong once the host's 60s RandomBarIndexHelper tick advances the host's stream.
- Answer to the //todo: is this networked? at line 329: yes. GameObject.Enabled of a network root is snapshot-replicated (NetworkObject.cs:655 / :890).

---

# DONE

---


Read all of Code/, the scene/prefab JSON, and the engine networking source. Findings, worst first.

1. Bar.cs:113,149 — IsProxy is used as "am I the player sitting down". It isn't.
   Bars are scene objects with NetworkMode: 1 (Object), network-spawned unowned at scene load (Scene.LoadSave.cs:152 → NetworkSpawnRecursive(null)). UpdateIsProxy() clears IsProxy when IsUnowned && Networking.IsHost (NetworkObject.cs:77), so inside this broadcast IsProxy == false only on the host, for every player.

Consequences:
- Non-host clients never enable their bar cam and never get minigame.BeerSpawnLocation, so their minigame view and beer spawns are broken.
- When a remote player sits down, the host's camera switches to that player's drinker seat and the host's BeerSpawnLocation is overwritten.
- Invisible solo, because Networking.IsHost is true when System is null (Networking.cs:201).

Fix: gate on playerConnection == Connection.Local, not IsProxy. Same in SitUpPlayer.

2. GameManager.cs:490 — ResolveLocalPlayerHelper doesn't refresh _LocalPlayerRigidbody or _LocalArrowIndicator.
   It repairs _LocalPlayer / _LocalDrunkCC / _LocalPlayerProgress only. Those two are set exclusively in SpawnPlayerHelper, which runs host-side in OnActive. So:
- On a client, _LocalPlayerRigidbody is null forever → ResetPlayerHelper logs an error and returns at line 437. EndMiniGameHelper already disabled the player (StartMiniGameHelper:329), so after the first minigame a client is permanently disabled and un-respawnable. Same for pressing R. No arrow indicator either.
- On the host, _LocalPlayer gets repaired to the host's own player but _LocalPlayerRigidbody still points at the last-joined player, so ResetPlayerHelper teleports your object while zeroing someone else's velocity.

3. DrunkCC has no ownership guard at all.
   OnFixedUpdate → HandleRunning() runs on every client for every player object and reads local Input.Down("Left"/"Right"), Input.Pressed("Jump"). Every client applies its own input as forces/torques to remote players' rigidbodies, calls ApplyDebugLocks (which writes rb.WorldRotation directly), and can trip EnterKnockedDown() on a proxy from a roll value it computed itself. Proxy rigidbodies are simultaneously being driven by _body.Move(Transform.TargetWorld, ...) (Rigidbody.cs:639), so this is a fight, not just waste. Needs an early-out: if (!GameObject.Network.IsMine()) return; in OnFixedUpdate (and OnUpdate, which unconditionally drives the animgraph).

Related: CurrentState isn't [Sync], so a knockdown never replicates — remote players never visibly ragdoll. BeerLevel isn't synced either while LeaderboardEntry has a Beers field.

4. Bar.SitDownPlayer — patron count leaks, dictionary can throw.
   _CurrentPatronCount++ at line 81 runs before the availableDrinker == null early-return at 96, so a full bar permanently inflates the count. SitUpPlayer was already fixed for this ordering; SitDownPlayer wasn't. Line 106 uses Dictionary.Add, which throws ArgumentException on a re-entry for the same connection — inside an RPC. Use the indexer.

7. Drinker assignment can desync across clients.
   SitDownPlayer picks "first _Drinkers[i].Enabled == false". Two clients sitting at the same bar in the same window produce two broadcasts whose relative arrival order isn't guaranteed to match on every receiver, so client A can hold seat 0 on the host and seat 1 on client C — and _connectionToPatronIndex disagrees. Host-assigning the seat index and passing it in the RPC removes this; it's the one place where host authority is genuinely cheaper than fixing it client-side.
