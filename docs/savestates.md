# Savestates - how they work

The detail behind CLAUDE.md *Key concepts - Savestates* (moved out 2026-09-26). IL and game internals are in game-notes *Saving and loading*.

- **Savestates** (no tab: Practice start states + the bridge; practice-only; game-notes *Saving and
  loading* has the IL). The game's own level serialization
  (`LevelSerializer.SerializeLevel`) written to
  `config/ForestOverlay/savestates/*.fosave` — never a save slot or Steam
  Cloud. Capture mirrors the game's save routine. Two restores:
  - **Quick load** = in place (~0.15 s on a fresh heap, no load): `LoadNow`, plus what
    `LoadNow` does not do for a full-level save — delete objects not in the
    save (walls built since; **never weapon-upgrade receivers**, v0.22.7),
    clear their build-mission HUD line, stash held items — and
    `Game/PickupKeeper` puts taken world pickups back. Afterwards the
    **cave state is sent outright from the file's `cave` flag**
    (`GameBridge.ForceCaveState`): the serializer restores the flag without
    its effects; since v0.24.123 it also switches the cave mouths' black
    walls as walking in / out does. Cave panels broken since come back
    from one intact copy each (`Game/PanelKeeper`, v0.24.123: a swing
    ran `CutDown` twice and the second, piece-less copy was rebuilt).
    Restart closes the game's pause menu / inventory first
    (`Game/MenuClose`, v0.24.123: their timeScale 0 stalled the restore). Spears and limbs left since the capture are removed;
    trees chopped since regrow, bushes / saplings cut since come back,
    and their new logs / sticks go (`Game/NatureKeeper`, v0.24.61-62); boss
    Megan is put back seated when she was at capture (`megan` header,
    `Game/MeganKeeper`, v0.24.35-37; game-notes *Megan after a Quick
    load*); the endgame elevators and active area as at capture
    (`ElevatorKeeper`, `AreaKeeper`, v0.24.40-41; a ride under way is
    stopped first, v0.24.64).
  - **Full load** = with a scene load (~5-15 s): `LoadSavedLevel` — the
    second half of the game's own load. Afterwards (v0.24.25-0.24.28): the
    player is held at the captured spot until every scene loaded at
    capture is back, the endgame area is force-loaded if the capture had
    it, placed pickups taken before the capture are removed, the captured
    cannibal families are rebuilt, the held items are equipped again
    for the animator (v0.24.43), bushes / saplings cut at capture are cut
    again (`cutbushes`, v0.24.65-66).
  Both: a sun still out of step with the restored time is snapped
  (`Game/SunSync`, v0.24.67). A cutscene capture is fast-forwarded (25x) with the held weapon's
  memory put back (`heldbefore`) and its sounds kept in step
  (`Game/CutsceneAudio`, v0.24.36).
  **From another save** (sharing): every game gives its objects its own
  `UniqueIdentifier` ids, so an in-place restore first **adopts** the saved
  ids — every live identifier the save lacks takes the id of the saved
  object with the same name, prefab class and parent id, shallowest first,
  unique matches only; identical siblings pair in order
  (`SavestateBridge.AdoptPlayer`; unmatched ones are named as `other
  misses:`). **Refused across Creative and survival** (the mode is not in
  the save) unless *Allow restoring across Creative and survival (testing)*
  is on (`AllowCrossModeRestore`, off; Debug views).
  The file header lists the world pickups at capture and whether streaming
  was unloaded; `Data/SavestateFile` is pure and tested. Sticks / rocks
  around pooled trees are given back as captured (`greebles` header,
  `Game/GreebleKeeper`, v0.24.70; game-notes *Greebles*). **Enemies**:
  capture writes `families` / `enemies`; after a Quick load on the
  surface and after a Full load, `EnemyKeeper.Rebuild` runs the game's
  `startSetupFamilies`, builds each captured family and places every
  member by kind with its health (sleepers back asleep); a cave capture's
  cave families are kept and moved back (`RestoreCave`). Open: does
  `updateSpawns` top up a random family; weapons are whatever the spawn
  gives (game-notes *Cannibal kinds and families*). Restores are refused
  at the title screen (v0.24.73).
