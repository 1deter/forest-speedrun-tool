# Community packs

Spots and timed segments every ForestOverlay install downloads on its own
(a few seconds after the game starts, or **Practice -> Import -> Check
community now**). They appear in the Practice list under **Community**,
read-only; **Duplicate** makes a runner's own editable copy, start state
included.

## The demo

`demo-template.foseg` is a template, not a real route: its `#` comments
describe every section and key. It shows under Community as "Demo - plane
crash dash". Delete it (and re-run the script) once real packs are in.

## Adding or changing a pack

1. In game, select the entry in the Practice tab and press **Export** (Share
   row). Leave "with my attempts" unticked - packs carry spots, not times.
2. Take the file from `BepInEx/config/ForestOverlay/shared/` and put it in
   this folder. Give it a plain name: letters, digits, `-`, `_`, `.`,
   ending in `.foseg` (other names are ignored by the plugin).
3. Ids are hidden and random since v0.24.74 (`s-7f3a9c2e1b4d`) - nothing
   to choose. An entry made before that has an old-style id
   (`spot.my.new-spot-3`) that other runners' own first spots share, and
   the plugin skips a pack entry whose id a runner already has: give such
   an entry a fresh random id first (and move its `runs/<id>` folder and
   start state file to match).
4. Run `python scripts/community-index.py` - it rewrites `index.txt` (file
   name + hash). The plugin downloads only files whose hash changed.
5. Commit both. Runners get it on their next start. CI fails if a pack
   does not parse, two packs share an id, a pack carries attempts, or the
   index is stale (`tests/.../CommunityPacksTests.cs`).

Removing a file (and re-running the script) removes the entry from every
install. A runner who already has an entry with the same id keeps their
own; the pack's is skipped.

Start states are game save data the plugin loads - review a pack before
merging it.
