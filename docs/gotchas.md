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

52. **A hook can run twice before the Destroy lands.** `PanelKeeper`
    copied a cave panel in a prefix on `CutDown` so a restore could put
    it back. A swing hits the panel's two colliders in one physics step,
    so `CutDown` ran twice; the second copy was taken after the game
    had pulled the pieces out, and the restore rebuilt that one - a
    panel that threw on every later hit and never broke (runner Tom,
    v0.24.112-123). A pair of identical log lines is the tell. Key a
    "do once" hook on the instance, and check a copy is whole before
    using it.

53. **A game's own database can be wrong - check it against the live
    objects.** `PassengerDatabase._passengers` lists every passenger
    with a scene path, and it looked like a ready-made "where is it"
    guide. Five `PassengerView`s in Cave 6's main cavern carried ids
    18, 14, 4, 20 and 6; the database put those in Caves 1 and 9
    (v0.24.130, bridge). Editor-generated data goes stale when scenes
    move on. Spot-check a few entries live before shipping anything
    built on it.

54. **Drive a UI the way the game does.** Switching the survival book
    to a nature page with `SetActive` on the page object left the main
    index drawn over it (the author spotted it in the screenshot,
    v0.24.132) - a link click (`SelectPageNumber.OnClick`) also turns
    off the other layers. Poking objects is fine for reading; for
    showing, go through the game's own path, and when a screenshot
    looks wrong, suspect the poke first.

55. **Read what the fallback changes, not only why it fires.** The
    plane axe message was chased as a race for days; the IL of the
    fallback (`AddItemNF` at the cap: HUD message, return false) showed
    it changed nothing - the message was the whole symptom, only our
    restores reach it, and hiding it in that window was the complete
    fix (v0.24.129). Before hunting the cause of a fallback, check
    whether its effect matters.

56. **`Camera.CopyFrom` copies the Camera only.** Freecam spawned its
    own camera with `CopyFrom` and looked darker, with no arms, for many
    versions: the game's look (Sunshine shadows, atmosphere / fog,
    post-processing, SSAO, clouds, water - ~20 components on
    `MainCamNew`) lives in sibling components, which stayed on the
    disabled original. v0.24.136 flies the game's own camera instead,
    parking its gameplay children (Grabber, hitTrigger, water sensor) on
    a stand-in and pausing `SimpleMouseRotator` / `PlayerCamLocation`.
    `inspect camera` before copying any game object's behaviour.

57. **The player's things are not all under the player.** The
    inventory's item views live under `PlayerInventory._inventoryGO`, a
    scene root of its own (`INVENTORY`, parent null), like held models
    unparented by `FakeParent`. A restore from another save deleted them
    as "not in the save"; their `InventoryItemViewsCache` lists were left
    empty, `CraftingCog.IngredientCleanUp` reads `[0]` of each, and
    `PlayerInventory.Close` threw before `timeScale 1` - the inventory
    could never close (runner Ruben, v0.24.134; fixed v0.24.137). Before
    deleting or resetting "everything outside the player", list the
    player's other roots.

58. **Switching a camera off changes which camera Unity calls
    "current".** Unity keeps the last camera drawn as its current camera
    into the next frame's `Update` (`Camera.current` reads it there). In
    Unity 5.6, setting `targetTexture` on the current camera outside
    rendering writes through a null render context: a native crash
    (`Camera::SetTargetTextureBuffers`, write to `0xf8`). The terrain
    grass camera patch switched a depth-0 camera off mid-load, leaving
    the grass controller's depth -1 camera last; the controller then
    rebuilt its texture and the game died after a death reload out of
    the Megan fight (v0.24.137). The current camera only changes at the
    next render: a guard that switches the camera back on earlier in the
    same frame does not make the rebuild safe, so the guard at the crash
    site must not depend on it (v0.24.138-139 still crashed; fixed
    v0.24.140). A native crash is
    readable: Unity's symbol server has the player PDB
    (`symbolserver.unity3d.com/player_win_x64.pdb/<guid><age>/player_win_x64.pd_`,
    `expand` it), and the PDB's public symbols plus a stack scan of
    `crash.dmp` name the function - no debugger needed
    (`scripts/symbolize-crash.py`; `scripts/sample-stacks.py` samples or
    stack-walks the live game's threads the same way).

59. **Log the work, not the queue.** A work queue shows what waits, not
    what runs: `graphUpdateQueue` held the same stale item through three
    restores (its `_version` never moved) while the real 16-60 s update
    ran on a thread. A read-only prefix on the enqueue (`AstarPath.UpdateGraphs`:
    bounds + caller) named it in one launch - one update of 1533 x 1407 m,
    a game merge across the map - after hours of live reads and IL
    theories (v0.24.141-143). Also: a game list that is appended to and
    merged each time must be checked for ever being cleared
    (`dummyNavBounds` never was).

60. **A config write saves the whole file.** Setting a BepInEx
    `ConfigEntry.Value` writes `com.deter.forestoverlay.cfg` at once: a
    bridge `set ..._panelOpacity.Value` took 86 ms. A slider or a text
    field that writes on every change hitches on every step / keystroke.
    Keep the value in the module while it changes and write once after it
    settles (the splits opacity slider and runner name write 0.5 s after
    the last change, v0.24.150); drags write on mouse release.

61. **A sentinel inside the value's range is reachable.** The splits
    panel's `PanelX = -1` means "against the right edge". Dragging past
    the left edge made the live x negative, which read as the sentinel,
    and the panel jumped to the right side (author, v0.24.151, windowed).
    Clamp live input to the real range, and apply a sentinel only to the
    saved setting, never to a value being edited (v0.24.152).

62. **A fixed list is a guess about what runners care about; the game already
    knows what changed.** v0.24.160 recorded 16 hand-picked items as 5 Hz
    channels; the author asked the next hour for *every* item (coins,
    tapes ...) and for reads only when the bag changes. v0.24.161 records
    changes only, and reads only after a game method that writes the value
    ran (ilscan `writes <Type>::<field>` lists them all; postfix each, plus
    a slow full read as a net). Before a per-sample reader of game state,
    ask: can the list be "whatever is there", and is there a write to hook?

63. **An image read at the same path can be the old picture.** Testing
    whether the ocean draws in the aerial capture, three captures of one
    tile written to the same `canopy/0_0.jpg` looked identical, and the
    session concluded "hiding the ocean changes nothing" (2026-09-28). The
    Read tool had shown a cached copy; a shot under a new name showed the
    difference at once. Give every test shot its own name before comparing.

64. **A Unity PPtr's file id is relative to the file holding it.** The
    offline world export keyed meshes by `(m_FileID, path_id)`: the same
    pair in another scene is another mesh, so cave pickups were drawn with
    an endgame mesh at 12x - 1.7 km planes across the map. Resolve the id
    through the referencing file's externals (`world-extract.py`
    `ref_key`), and note UnityPy's attribute is `pptr.assetsfile`, not
    `assets_file` (a `getattr` default hid the typo).

65. **A release chain must stop when a step fails.** A Python edit
    (heredoc) failed its assert, but the next `dotnet build && sed ... &&
    git tag` ran anyway: v0.24.172 shipped as a bare version bump with a
    changelog line for a fix it did not contain (v0.24.173 carried it; the
    .172 entry now says so). Join the edit to the release with `&&`, or
    check the edit's output before bumping.

66. **Uploaded files cached for a day need a version in their URL.** The
    photo map re-upload still showed black corners: browsers and
    Cloudflare kept day-old tiles (`max-age=86400`) from before. Every
    uploaded set (aerial tiles, 3D world) now carries a build stamp in its
    json (fetched `no-cache`) and every file URL `?v=<build>`.

67. **Scene files hold placeholders, not the world.** Trees, bushes,
    rocks and the caves' walls are spawned from pools near the player by
    LOD scripts (`LOD_Base.High/Mid/Low`, at `_position` and the
    placeholder's rotation, the prefab's own scale - checked live on
    trees and rocks; **not the caves**, gotcha 69); a
    scene read shows none of them. An in-game dump of the placeholders
    (`Game/WorldDump`) + the meshes read offline by name and vertex count
    gives the whole world. Also: a shader's look can come from more than
    `_MainTex` - the Lux rock shader lays snow / grass over faces that
    look up (`_WnAlbedoSmoothness`), and keeps smoothness, not a cut-out,
    in its alpha.

68. **A texture's size on screen is the mesh's UVs times the material's
    tiling - check both, and look before claiming a fix.** (2026-09-28, the
    3D world's Cave 6.) The cave shells' UVs span ~0.6 of one texture over
    200 m; the materials tile it 35-40x (`m_Scale` of `_MainTex`), which the
    export dropped - fixed in 2056f43. Two fixes were pushed as "fixed" on
    offline reasoning alone (back-face culling, then the tiling) and the
    author still saw the cave broken both times: render the local site at
    the spot and compare with a game `shot` before telling the author.

69. **A subclass can override the spawn's scale.** (2026-10-01, the 3D
    world's caves, the third try.) `LOD_Base.SetLOD` spawns the prefab at
    its own scale, but `LOD_Cave.SetLOD` (and `LOD_CaveEntrance`,
    `LOD_CaveMedium` / `Small`) then sets the piece's `localScale` to the
    placeholder's `lossyScale` - 0.2 to 50 in the caves. Gotcha 67 had
    checked the scale live on trees and rocks only, so every cave piece was
    exported at scale 1: walls 2-2.5x too big (the "big grey rock"), ledges
    tiny. Before trusting how a base class places things, list the
    overrides (`ilscan type <Base>` subclasses, `ilscan refs
    set_localScale`) and compare one spawned object of each kind live with
    its placeholder (`get #h LOD_Cave.CurrentLodTransform.lossyScale`).

70. **An object's origin is not where its mesh is.** (2026-10-01, the 3D
    world's cave floors.) The cave grounds (`Cave06_Ground_Collision`,
    `Cave0N_Ground_inside` ...), the mountains and the sinkhole sit at
    (0, 0, 0) with world-space vertices. The export filed instances into
    250 m chunks by their origin, so every one of them landed in the chunk
    at the world's centre, and the site (loading within 700 m of the view)
    never showed Cave 6's floor or collision 1.3 km away - "the floor still
    seems absent" after two fixes to the walls. Place by the mesh's world
    bounds and keep each chunk's real reach (`bb`); for a hole in a model,
    first ask `Physics.OverlapSphere` in game what is there (the bridge
    lists the colliders), then look for that object in the export.

71. **A `?v=` the server ignores protects nothing - and reproduce on a
    fresh load before blaming the data.** (2026-10-01, the 3D world.) The
    author saw Cave 6 full of leaves and big grey shards 3 minutes after a
    world upload; the handoff blamed the export's asset matching (name +
    vertex count). Wrong: every mesh / material near the spot had exactly
    one candidate, and a fresh load of the live site looked right. The
    author's tab had been open since before the upload: `world3d.js` read
    `world.json` once per page (the site navigates without reloading), the
    files are **named by index**, and the static server ignored `?v=` - so
    the old index got the new upload's files at those numbers (a tree's
    leaves where a cave wall was), cached a day under the old URL. Fixed
    (`MetaBuild`): another build's `?v=` - or any while an upload runs (the
    json goes last) - is a 404 with `no-store`, and the page re-reads the
    json on each 3D view and starts over on a refused file. Lessons: a
    version in the URL only helps if the server checks it whenever the
    content behind a name can change; and when a report shows what fresh
    renders do not, ask *when* and *how long the page was open* - the
    author's screenshot is in the previous session's transcript (`type:
    image` in its `.jsonl`) even when the handoff says it was not saved.

72. **A check per row is not a check per thing.** (2026-10-01, the
    website's security review.) The site let only a route's owner change
    its name - per row. But a spot is many routes under one segment id,
    and the spot page shows the labels of the route run most recently: a
    runner who took someone's id, moved a zone (a new route, their own
    row) and ran it, renamed the spot and became its "by". The fix put
    the check at the level of the thing people see: a copy takes the
    spot's labels and owner. Whenever rows are folded into one shown
    thing (newest wins, first wins, a join), ask who can create a row
    that wins - not only who can edit an existing one.

73. **Diff a switch's two outputs before shipping it.** (2026-10-01, the
    photo map's Water button.) v0.24.170 captured every coastal tile a
    second time with Ceto's ocean switched off, the bake made "-dry"
    layers of them and the site a Water button - which "did nothing"
    (author): the two pictures differed by JPEG noise, because the ocean
    never draws inside the capture at all. The same capture "switched
    off" eye adaptation, which the game turns back on from OnGUI (gotcha
    1 again), so every tile kept its own exposure - the map's brightness
    bands. Both were one numeric check away: the mean difference of the
    on / off outputs, and the setting read back a second later. When a
    feature is "the same thing with X off", compare the two results
    before building a layer, a button or a release on them.

74. **A check against a clamped result must clamp its input too.**
    (2026-10-01, the 3D map's white flicker.) The 3D view's detail patch
    follows the camera: "is the centre covered? if not, build a patch
    round it". The patch is clamped inside the map; the coverage test was
    not - so with the centre within ~500 m of the map's edge (the whole
    coast) or past it, the clamped patch never "covered" it and was
    rebuilt every 0.4 s, each time remaking both terrain photos (white
    while they uploaded). 28 rebuilds in 16 s, measured on the old code;
    once on the new. Whenever a target is clamped, test against the
    clamped target (or compare the new result with the current one),
    or the condition can never hold.

75. **Switch layers off before fixing what a symptom looks like.**
    (2026-10-01, the 3D map's "dark water over land".) The backlog blamed
    the lakes' models (larger than the lakes, over a coarse terrain) and
    a ground clip for them was written first. Turning the models off
    left every "lake" in place: they were the sea plane - one flat plane
    at sea level, filling every pit below it inland (the sinkhole and the
    dips round it). The fix was the photo bake's own rule (sea only where
    it is open to the map's edge). For a render artefact, hide each layer
    in turn (models, sea, patch, world group) and see which one takes it
    away; a raycast that hits nothing says it is not a model.

76. **A game can have more than one distance switch.** (2026-10-01, the
    photo map's black lakes.) The aerial capture raised LOD_Manager's
    ranges and called detail "even to the tile's corners" - but 963
    objects (every lake among them) switch by `LOD_GroupToggle`'s own
    distances, which LOD_Manager never touches. A lake 150 m from the
    tile's centre drew its black stand-in, cut at the tile's edge. Before
    scaling "the" LOD distance, list every type that compares something
    with the player's position (`ilscan refs PlayerCamLocation::PlayerLoc`);
    a shape cut exactly at a tile edge means a switch on the tile's
    centre.

77. **A scene object can be moved at run time.** (2026-10-01, the 3D
    yacht.) The scene file had the yacht 130 m from where it floats: the
    game reparents the scene's `Yacht` under a spawned prefab and moves
    it; the export placed it from the scene file, on the shore. The tell
    is a scene object (positive handle) under a spawned root (negative
    handle). Compare an exported object's position with the live one
    (`find`) before chasing its look; export moved objects from the live
    game (`WorldDump.Placed`).

78. **A folder read whole turns a diagnostic dump into data.** (2026-10-01,
    the 3D lab.) The export reads every `placed-*.txt` in the world dump
    folder; 28 `WorldDump.Placed` dumps made to diff the lab went into two
    exports as duplicate geometry, and the diff against those exports
    matched the dumps against themselves (5857 "matched", 5690 really).
    Write diagnostic dumps under a name the reader skips, or move them out
    before the next run; check an improvement against a clean input.
    Same session: key "the game lists this object" on its path, not its
    place - props physics nudged (whiteboards and their drawings) did not
    match by position.

79. **A component that sets itself up once misses an in-place load.**
    (2026-10-01, the Quick load audit.) TickOffSystem and SurvivalBookTodo
    apply their saved state in `DelayedAwake`, guarded by `_initialized`;
    LoadNow in place calls it again and it returns at once. The nature
    guide kept ticks found after the capture; the to-do list got new,
    never-prepared task objects and stopped updating. A save field coming
    back is not the state coming back: grep the component for
    `_initialized` / `DelayedAwake` and read what runs only the first time.

80. **Serializing has side effects.** (2026-10-01, the Quick load audit.)
    `OnSerializing` hooks write live fields: with the survival book open,
    PlayerInventory writes its stowed hands (`[0, 0]`) into
    `_equipmentSlotsIds`, so a capture then (or a JSON dump for a
    diagnostic) records nothing held. Take a diagnostic dump in the state
    you are not testing, and decide what a capture should record from the
    live objects, not from what the serializer wrote.

81. **A world object the player carries is saved where its parent puts
    it.** (2026-10-02, v0.24.201.) Pushing a log sled parents its root
    under the player (local (-1.32, -1.3, 3.155)) and destroys its
    Rigidbody; the save writes the local position, so a restore of a
    capture taken while pushing put the sled near the world origin,
    without a Rigidbody (seen only once a real sled was built). The same
    session: the capture copied the game's save order and dropped a held
    glider at its start - a capture in flight ended the flight. When an
    action holds something, read what it parents and destroys
    (`ilscan body` of its connect / exit), capture during it, and look at
    the object after the restore, not only at the player.

82. **An integrity check must know what the platform and the game do
    themselves.** (2026-10-02, v0.24.207-209.) Run mode's first report
    called BepInEx's own patches (Console.SetOut, Assembly.LoadFile /
    LoadFrom / get_Location / get_CodeBase, Trace.DoTrace - preloader and
    interop fixes, some under `harmony-auto-<guid>` ids) "another mod",
    and a Creative save's GodMode / InfiniteEnergy / NoSurvival (set by
    `GameMode_Creative`) "a game cheat". The checks also kept running
    after the attempt ended, so the save loaded after a reset was charged
    to it. Before trusting a "NOT OK", run the check on a clean install
    and in every game mode, and stop watching when the thing watched ends.

83. **A picture can depend on load order - test the old build the same
    way before blaming the new one.** (2026-10-02, the website's version 3
    world.) A phone shot of the tree spot differed in 8.5% of its pixels
    after the repack, and bisecting pointed at one dead body's LOD - which
    cannot touch maple leaves. Its file only changed when that model was
    built. Holding back random files on the *old* site flipped the same
    leaves between the same two looks: every InstancedMesh sits at the
    origin, so three.js's draw order is creation order, which is arrival
    order. Compare shots taken the same way (single views), and when a diff
    looks impossible, rerun the before build with delayed files
    (`site-measure.py` `DELAY`) - two stable looks = an existing ordering
    effect, not the change.
