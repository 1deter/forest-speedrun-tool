# Community packs

Spots and timed segments every ForestOverlay install downloads on its own
(a few seconds after the game starts, or **Practice -> Import -> Check
community now**). They appear in the Practice list under **Community**,
read-only; **Duplicate** makes a runner's own editable copy, start state
included.

## Adding or changing a pack

1. In game, select the entry in the Practice tab and press **Export** (Share
   row). Leave "with my attempts" unticked - packs carry spots, not times.
2. Take the file from `BepInEx/config/ForestOverlay/shared/` and put it in
   this folder. Give it a plain name: letters, digits, `-`, `_`, `.`,
   ending in `.foseg` (other names are ignored by the plugin).
3. Give the segment a lasting, namespaced id before exporting
   (`deter/cave5-practice`, not `spot.my.new-spot-3`): the id is how times
   are compared, and renaming it later orphans every recorded attempt.
4. Run `python scripts/community-index.py` - it rewrites `index.txt` (file
   name + hash). The plugin downloads only files whose hash changed.
5. Commit both. Runners get it on their next start.

Removing a file (and re-running the script) removes the entry from every
install. A runner who already has an entry with the same id keeps their
own; the pack's is skipped.

Start states are game save data the plugin loads - review a pack before
merging it.
