# Gotchas learned the hard way

The full story behind each lesson indexed in CLAUDE.md (*Gotchas*). Numbers are stable - commits and docs cite them ("gotcha 25").


1. **The game re-asserts state every frame — use its flags, don't fight it.**
   `timeScale` is overwritten by `InventoryItemView.Update`. The cursor is
   overwritten by `VirtualCursor.LateUpdate`, which *warps the pointer to
   screen centre*, so no later write can fix it. Both are solved by setting
   the game's own flag (`Input.IsMouseLocked`, `FirstPersonCharacter.LockView`).
   "Win the frame" is not a strategy; find the flag.

2. **`OnGUI` runs several times per frame.** Never allocate in it.

3. **A throwing `Awake` silently kills the plugin** while BepInEx still logs
   "loaded". Every lifecycle method is individually try/caught.

4. **Don't trust assumed names.** Everything in `src/Game/` was confirmed from
   a dump or IL. `docs/game-notes.md` once contained a guess that was wrong
   (`VirtualCursor` filed as "gamepad-related"), and that guess cost a release.

5. **The F11 dump only sees reflection metadata.** For behavioural questions —
   what writes this field, what runs every frame, which method to hook — use
   `tools/ILScan`, which reads real IL offline:
   ```bash
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll writes "UnityEngine.Cursor"
   ```
   `strings` finds `SendMessage("name")` callers, which `refs` cannot see.

6. **Cached component references go stale across a save load.** Unity's
   fake-null makes them look merely absent. Re-resolve, and prefer the game's
   statics (`LocalPlayer.Inventory`) over `FindObjectOfType`.

7. **Edge semantics matter.** A start zone fires on *crossing* (you spawn
   inside it); checkpoints and ends fire on *entry*. Getting this wrong made
   the clock never start.

8. **Test against real payloads, not remembered ones.** GitHub's API
   pretty-prints (`"name": "x"`); the asset lookup matched only the compact
   form, so no update ever downloaded while the version check looked
   healthy. `tests/.../ReleaseJsonTests.cs` holds a trimmed real response.

9. **Never round-trip text through Windows PowerShell 5.1**
   (`Get-Content | Set-Content`). It reads BOM-less UTF-8 as cp1252 and turns
   every `—` into `â€”`. Edit with the editor tools, Python with an explicit
   encoding, or `sed`. Check: `git grep -n -I -P 'â€|Ã|Â' -- ':!CLAUDE.md'`.

10. **Anything that only reaches a machine via `deploy.ps1` is missing for
    runners.** The 100% list did exactly that. Ship data inside the DLL.

11. **`Resources.FindObjectsOfTypeAll` walks every loaded object.** Calling it
    on a 1 Hz refresh was a visible once-a-second stutter. Find a component
    once, keep it, re-search only when it goes fake-null, and rate-limit the
    search (there is nothing to find at the main menu). `ModuleHost` logs any
    module Tick over 5 ms as `Slow tick: '<id>'` — check the log for it before
    guessing at a hitch.

12. **`OnRenderObject` runs once per camera**, reflections and UI included.
    GL overlays check `DrawTarget.ShouldDraw()` so a long run line is drawn
    into the view only. The `Perf (30 s):` log line counts passes drawn and
    skipped.

13. **Search `strings` for every method of an action, not just its entry
    point.** Cutscenes are started by `SendMessage("routine")`, which `refs`
    cannot see. The red elevator sends `openDoorRoutine` directly, bypassing
    the `openKeypadDoor` that was hooked — found only from a real run's log.

14. **Labels from memory are guesses too.** game-notes had "Approaching
    Megan" and "Megan into artifact" swapped (inherited from an earlier
    session). A real endgame run caught it. Anything a runner will see by
    name — split labels especially — gets confirmed against a log.

15. **Unity 5.6's `UnityWebRequest` does not treat a 404 as an error.** Check
    `responseCode` yourself; a 404 body arrives as ordinary data.

16. **The runner's `LogOutput.log` is the test harness** - and since
    v0.24.13 the **live test bridge** is the other one (see *The live
    test bridge*): ask the running game directly instead of guessing
    from IL. There is no game
    here to run. The author tests in game and gives the path
    `G:\SteamLibrary\steamapps\common\The Forest\BepInEx\LogOutput.log` —
    read it directly. So every new mechanism logs one line when it acts
    (`Game event:`, `Death (...)`, `Teleport to ...`, `Perf (30 s):`), and
    that line is what gets asked for. Log what a hotkey **acted on**, not
    just that it ran: F7 restarted a different spot than the one the
    author had just set up, and only a `Restart '<id>'` line would have
    shown it.

17. **A library method that "does X" may only do X in one mode.**
    UnitySerializer's `LoadNow` deletes objects missing from the save — but
    only for a partial save (`rootObject` set), never for a full level, so
    walls survived the first in-place restore. Read the whole method body
    (`ilscan body`) before building on what its name promises.

18. **Static or instance: check before binding.** `ItemDatabase.ItemById`
    is static; binding it with instance flags found nothing, and every held
    item read `item <id>` for months. `ilscan type` marks static methods
    (since 2026-09-23); pass `BindingFlags.Static` when it says so.

19. **Tooling: multi-line edits go through a script file.** Long
    `python - <<'EOF'` heredocs in the Bash tool failed with quoting
    errors; write the script to the scratchpad and run it. Inside a
    triple-quoted Python replacement, `\n` meant for C# becomes a real
    newline — use the Edit tool for C# string literals with escapes.
    Use raw strings (`r'''...'''`) in those scripts so C# escapes survive.

20. **A restore can bring back a flag without its effects.** The serializer
    restored `IsInCaves = false` while the cave's side effects (no terrain
    collision, cave streaming) stayed, and `GotoCave` does nothing when the
    flag already agrees — so a death in a cave restored "outside" and fell
    through the world. When restoring state, send the game's own message
    for the state you want (`InACave` / `NotInACave`), don't test the flag.

21. **Ids are per game, not per scene.** `UniqueIdentifier` ids (GUIDs)
    differ between saves for the player **and** for objects outside him
    (the inventory's item views). Anything cross-save — shared start
    states, cloud runs — must map ids, not assume them
    (`SavestateBridge.AdoptPlayer`). Look at the restore log's
    `adopted / left (n on the player)` counts before guessing.

22. **A hook runs mid-method; the caller carries on.** The practice revive
    happens inside `PlayerStats.Hit`, called from
    `FirstPersonCharacter.HandleLanded` — which then played the stagger,
    froze input and scheduled a 1 s recovery. Read the **caller's** body
    past the hooked call (`ilscan body`), and when cancelling a sequence,
    apply its **end state** (the delayed routine's last lines), not just
    stop its start.

23. **Whose lock is it?** Opening the window over the ESC menu found the
    player already locked by the game; releasing it on close called
    `UnLockView` under the menu and hid the cursor. Before taking over a
    game state, note whether the game already held it, and hand back only
    what you took.

---

24. **A static walk cannot see every root.** The v0.23.0 census walked
    every static of the game and found nothing growing while the heap grew
    ~120 MB a load. Threads, live `DontDestroyOnLoad` objects' fields and
    generic-type statics are invisible to it, and counting objects hides
    one huge array (v0.23.2 adds sizes, DDOL roots and a thread count).
    The pathfinding theory it led to was wrong (gotcha 25); the thread
    count found two leaked threads a load (v0.23.3). When a census comes up
    flat, count threads, then read the teardown code (`OnDestroy`) of
    anything that starts one (`ilscan refs "System.Threading.Thread::.ctor"`).
    A worker parked on a wait handle that only a frame callback signals
    never sees its "stop" flag once the object is destroyed. And **ask
    what the title screen clears that a reload does not** - a menu trip
    freeing the memory pointed at `TitleScreen.Awake` ->
    `EventRegistry.Clear()` all along (v0.23.4).

25. **A theory built from IL alone is still a guess.** v0.23.1 shipped a
    fix on the belief that a same-scene reload wakes the new scene before
    destroying the old one, so `AstarPath.OnDestroy`'s
    `if (active != this) return;` skipped the cleanup. In game it never
    acted once in 21 reloads. Before shipping a fix for an ordering or
    lifecycle theory, **ship the log line that proves the theory first**
    (or with it), and read the whole lifecycle: `OnApplicationQuit` calling
    `OnDestroy` itself made the only hit a false positive at quit.

26. **A restore runs frames - judge the "before" state before it.** The
    in-place restore is a coroutine: it puts the player back and physics
    steps run, so a trigger at the captured spot fires *during* it.
    v0.24.36 checked Megan's trigger after the restore, saw it spent (by
    the restore's own trigger enter) and rebuilt her under the cutscene
    that had just started. Read what a fix-up depends on when the restore
    starts (`MeganKeeper.LiveSeated`, v0.24.37).

27. **Read the whole "removed" line, not just the item you fixed.** A
    cleanup that removes things can remove the wrong ones. The cave 5 coin
    fix (v0.24.51-53) also removed a bottle, the modern axe, skulls, a
    Timmy drawing and a photo; the coins were gone, so the fix looked
    right. Twice a first theory was wrong, and only the log's full
    `removed N (...: Cash x4, Coins x6, Skull x5, ...)` showed it. Before
    a removal ships, check that every item it names was really taken.

28. **Match what you compare the same way on both sides.** The
    capture's pickup list is a `HashSet` of keys: one object with two
    `PickUp` components is one entry. The live side counted components,
    so one of each pair went "unmatched" and destroyed the object
    (v0.24.54). The capture also skips identifier pickups; the live side
    must skip them too (v0.24.53). Before pairing two lists, read how
    each is built (dedup, filters, active-only).

29. **A value the game fills in later reads as a default at first.**
    `mutantTypeSetup.storeSkinnyBool` / `storeMutantType` are stored by
    `initDefaultParams` a few fixed updates after a spawn. Read at once
    (v0.24.45's faster placement), every skinny cannibal looked plain
    and nothing matched (`0 of 12 placed`); regular ones matched only
    because their kind is the default. Wait for the value itself, not a
    count (v0.24.48), and test on more than one kind.

30. **Check when a file is created before planning to copy it.** The
    plan was "copy the previous `LogOutput.log` on startup"; BepInEx's
    preloader truncates it before any patcher or plugin runs, so there
    is no previous log to copy. `Core/LogKeeper` mirrors the running
    log instead (v0.24.55).

31. **The UiText rule covers the HUD and every fixed label, not only
    tabs.** The info box drew 18 px lines in a 330 px box: a wrapped
    update message showed half its second line (v0.24.58), a key name
    lost its ends in a fixed button (v0.24.57). The bridge sweep found
    both in minutes: after UI work, `OpenMyTab` on each module + `shot`
    and look, and push a long value through (`set ..._checker.Message
    "<long>"`) to see how a line wraps.

32. **Look for the game's reverse operation before writing one.** Trees
    had no "un-cut" in the save code, but `TreeLodGrid` has
    `RegisterTreeRegrowth` beside `RegisterCutDownTree`, and its one
    caller (`ShelterTrigger.CheckRegrowTrees`, sleep regrowth) is the
    exact recipe (v0.24.61). Search the counterpart's name (`Regrow`,
    `Respawn`, `Reset`, `Restore`) with `ilscan refs` / `type` first.

33. **Copying a scene object: local transform, woken state, copied
    flags.** `Instantiate(go)` with no parent copies the *local*
    position, so the copy landed ~450 m away; its `OnEnable` had already
    cached that position, so moving it later did nothing until it was
    re-enabled. Copy under an **inactive** holder, `SetActive(false)`,
    reparent with local values, then activate. A copy taken mid-action
    also carries the original's runtime flags (`LOD_Base.isSpawned`
    true with no view = destroys itself on its first refresh): reset
    them to what `OnDisable` leaves (`NatureKeeper`, `PanelKeeper`).

34. **One teleport, many callers.** Go ran `AreaKeeper.ForTeleport`;
    the bridge's `tp` never did, so it left the endgame flag set and the
    surface lit like a cave (v0.24.61). When a fix hangs off a teleport
    or restore, `grep MoveTo(` and cover every caller.

35. **"Parity with Full load" stops where the save stops.** A Full load
    regrows every bush because bushes are not in the save - a limit, not
    a goal. A Quick load can give back the capture exactly (a bush cut
    before it stays cut, v0.24.62), so it does - and the Full load was
    then fixed up after the game's load to match (v0.24.65). Decide
    against the capture, not against what a Full load happens to do.

36. **A diagnostic read mid-rebuild reports the rebuild.** The "not at
    capture" line counted `Axe Plane x2` after every Quick load for a
    session and was filed as a leak; the listing ran while the old and
    the re-created plane wreck both had their axe active, a second
    before the old one was cleared (v0.24.63). Before chasing what a
    check reports, re-read the world a few seconds later (`find ... all`
    in a timed bridge script); list after the restore's own clean-ups.

37. **"Left alone" is not "stopped".** `ElevatorKeeper` skipped an
    elevator that was `_moving` at restore time; the ride's coroutine
    kept its pending step and lifted the car and the player 3-7 s after
    the restore - a runner saw it "half the time" (v0.24.64). When a
    restore meets a game action in flight, stop it (its coroutine) and
    apply its end state (gotcha 22), then put back the captured state.
    A log word like `left moving` in a runner's failures is the lead.

38. **Bookkeeping must survive the restores it serves.** The cut-bush
    list (v0.24.65) was cleared by a Quick load from another world, and
    a listed bush already gone was not re-recorded, so the next capture
    wrote an empty list (v0.24.66). Remove only what a restore actually
    undid; "already gone" still counts as cut. Test the chain (Full load
    -> Quick load -> capture -> Full load), not one restore.

39. **A pool object carries its first user's state.** A greeble zone on a
    pooled tree kept the seed and taken flags of the first tree that
    object served, so the same tree showed other sticks whenever another
    pool object served it (fix list 3, v0.24.70). When something "moves"
    between visits, compare the object's handle / pool clone name across
    visits before blaming the restore - `(Clone)001` vs `(Clone)002` was
    the whole story.

40. **A cutscene can parent the player - the save then holds local
    numbers.** The keycard cutscene puts the player under the card
    reader's `playerPos`; the serializer wrote the offset from it
    (-0.03, 0.02, 0.63), so a Quick load dropped the player near the
    world's origin and a Full load on the surface, where the load chose
    the cave state (v0.24.79). F7 hid it for months: the restart's
    teleport moved the player back. Test restores through the bridge's
    `restore` (no teleport) too, and compare where the player lands
    with the header's `position`. And **set a test up the way a run
    reaches it**: teleporting to the vault door skipped the trigger
    crossing that loads the endgame, so the first test showed an empty
    corridor the game never would (author: "might not be a fair test").

41. **Where in the frame a call runs decides what the game does next.**
    A coroutine resumes after every `Update`; the game's own UI click
    comes before `Create.Update`. `CreateBuilding` sets `LockPlace`, so
    from a coroutine the generic build icons (`Grabber.ShowPlace`) came
    a frame late - after the wall architect's `DelayedAwake` had closed
    them - and stayed (v0.24.84). When a call into the game behaves
    differently from the same call made by the game, look for one-frame
    guards (`LockPlace`, `ShownPlace`, `yield null`) and compare where
    each side runs; the bridge's `call` runs in `Update` and can hide it.

42. **Measure the measurement.** Two readings in the performance work
    were the tool's own doing: the Perf line matched a GC seen in a
    frame to that frame's length, so collections set off in our own
    Tick read as 11 ms pauses (fixed v0.24.87 - the longer of that frame
    and the next); and hooking 1588 methods with Harmony nearly doubled
    the GC pause (90 -> 165 ms) through the patches' own objects. When a
    number surprises you, first ask what the instrument adds, and
    measure baselines on a fresh launch with the instrument off.

43. **A switch can be latched off before you arrive.** v0.24.90's
    allocation tracker installed Mono's profiler correctly and counted
    nothing: `mono_class_get_allocation_ftn` had cleared the allocators'
    `profile_allocs` flag for good the first time the JIT compiled an
    allocation, long before BepInEx loaded (read from the machine code;
    v0.24.91 sets it back). When a hook into the runtime is installed
    and silent, look for a once-only guard that ran at startup - and
    ship the "did it see anything" check with the first version (a
    count of 0 is an answer, not a quiet week).

44. **Measure before the changelog claims a number.** v0.24.92's
    changelog promised "about 60% less garbage" from an estimate; the
    in-game A/B said about half, and v0.24.93 had to correct it (CI
    publishes the section with the tag, so it reached runners). A
    runner-facing number comes from a measurement of the released build -
    or the notes say what changed without a figure.

45. **A timed wait during a load is not the time it says, and a
    diagnostic can be the hitch.** The game's "0.6 s" `WaitForSeconds`
    in `LoadSave.Activation` took 0.56-1.35 s real: load frames are long
    and each counts at most `maximumDeltaTime` of game time. Time waits
    in real seconds (`activation steps:`), not from the IL's constant.
    And the plugin's own "heap after GC" line forced a full collection
    at the moment a load handed over control (v0.24.97) - every
    diagnostic that runs on an event must be checked for what it costs
    there (gotcha 42).

46. **A symptom that appears later can be a coroutine finishing.** The
    vault door cave looked half drawn after a `tp` out of the red
    elevator; the first A/B (screenshots 5 s after the teleport) said
    "not reproduced" because the ride's end, ~30 s in, was what broke it
    (author spotted the timing). Gotcha 37 again, for teleports: an
    action the player leaves keeps running. When something "sometimes"
    looks wrong, look again after every pending game timer could have
    fired (`_duration`, `WaitForSeconds`), not just once (v0.24.100).

47. **A restore that throws the player: ask what held the body.** maks's
    cave 4 start state launched him ~48 m/s upwards. The capture was taken
    on a rope, where the body is kinematic and ignores the rock around
    the hole; the save has no climb, so the restore left a free body
    inside the rock. The clue was in the numbers: the spot's yaw equalled
    the rope's rotation (7.72), and a plain `tp` there shoved the player
    3 m sideways. The fix-ups that hang off one action (the load's
    per-frame pin) can undo another - a Full load's rope entry had to
    move after the hold (v0.24.104-105). Other kinematic modes (zipline,
    sled, wall / cliff climb, hang glider) are not covered yet.

48. **A frozen frame can count as game time.** The endgame's 5 s load
    freeze looked like 5 s of run time. v0.24.107 shipped a switch
    labelled "a run is ~5 s shorter"; timing the door's cutscene both
    ways showed the same end (17.3 vs 16.7 s). `Time.maximumDeltaTime`
    is 9 in this game, so a long frame advances game time by its full
    length and cutscenes and timers run on "inside" it. Before claiming
    a freeze costs time, read `maximumDeltaTime` and time the event that
    ends it, not the freeze (v0.24.108 corrected the label).

49. **One heap reading after a load is not a trend.** "20 Quick loads
    then a Full load = +118 MB that stays" drove a day of theories
    (serializer caches, conservative roots, elevator physics). Read every
    few seconds for a minute and it falls back: every Full load holds
    the old world for 30-70 s, then frees it. Measure the live heap with
    `GC.GetTotalMemory(true)` over time, with a control (a Full load
    alone), before calling growth a leak; the bridge's reply time for
    that call is the full-GC pause (v0.24.108, game-notes *The heap
    across restores*).

50. **A camera costs its culling whatever it draws.** The game profiler
    said the game's scripts were 1.5 ms of a 5 ms frame and stopped
    there; timing each camera (`Frame (30 s):`, v0.24.114) showed six
    to eight auxiliary cameras at ~0.25 ms each - 2.3 ms - two of them
    drawing nothing anyone sees. Unity culls every active renderer
    (~20k on the surface) per camera: an empty culling mask still cost
    0.24 ms. Before optimising what a camera draws, count the cameras,
    and ask who reads each one's texture (`RenderProbe`, v0.24.116).

51. **The picture needs eyes.** Skipping the last screen camera
    mid-frame (disabled in an earlier camera's pre-cull) measured
    perfectly - 0 renders, 0.28 ms saved, normal bridge screenshots - and
    froze the author's screen: audio on, 230 fps in the log, the image
    moving only when they tabbed out. v0.24.119 shipped it; v0.24.120
    withdrew it. Anything that changes how or when the game draws gets
    the author's eyes (a notice: "does the picture move?") before a
    release, and a dev test that could freeze the screen runs for
    seconds, not minutes (the 100000-frame test ran ~7 min).
