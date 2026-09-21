# Practice locations

Every `.txt` file in this folder is a set of practice spots. They are copied
into `BepInEx/config/ForestOverlay/locations/` by `scripts/deploy.ps1`, and the
plugin loads and merges **all** of them at startup.

That means contributing spots needs no code change, no rebuild, and no
registration step — add a file, or add lines to an existing one, and the
practice panel (`F3`) grows to match.

## Format

One spot per line:

```
category | name | x | y | z | yaw | notes
```

- `yaw` and `notes` are optional
- blank lines and lines starting with `#` are ignored
- **use a dot for decimals, not a comma** — files are parsed with the invariant
  culture so they work identically on every machine

Example:

```
Caves | Cave 2 entrance | 123.00 | 45.00 | 678.00 | 90 | drop down on the left
```

## Contributing

1. Stand where you want the spot to be.
2. Open the practice panel (`F3`), type a category and a name, press **Add here**.
3. The line is appended to `my-spots.txt` in your *config* folder. That file is
   personal and is never committed.
4. To share, copy the lines you want into a file in *this* folder — grouped by
   route or category, e.g. `cave-routes.txt` — and open a pull request.

Keeping personal captures (`my-spots.txt`) separate from contributed sets is
deliberate: pulling an update can never clobber your own spots, and your
scratch spots never end up in a pull request.

## A note on run legality

Teleporting writes to the game, so it is **practice only**. Using any spot from
this list sets the sticky `PRACTICE` marker on the HUD for the rest of the
session. That is intentional — it must not be possible to teleport and then
forget it happened while recording.
