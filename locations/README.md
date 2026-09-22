# Shared practice spots

Every `.txt` file in this folder is a set of practice spots. The files are
**embedded in `ForestOverlay.dll`** and written into
`BepInEx/config/ForestOverlay/locations/` on startup, so a plain drop-in
install gets them — no script, no registration step. The plugin loads and
merges **all** of them into the Practice tab.

Spots and segments are one thing now. This folder is the simple,
position-only format for sharing spots; timed segments with triggers live in
`BepInEx/config/ForestOverlay/segments/` (format in the `README.txt` the
plugin writes there).

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

1. In game, stand where you want the spot: `F2` → **Practice** → **New** →
   **Save**. It is written to `segments/my-segments.txt` in your config
   folder, which is personal and never overwritten.
2. To share, copy the position into a line in a file in *this* folder —
   grouped by route or category, e.g. `cave-routes.txt` — and open a pull
   request.

Shipped files are rewritten whenever they differ from the copy in the DLL, so
never edit them in your config folder; your own files are left alone.

## A note on run legality

Teleporting writes to the game, so it is **practice only**. Using any spot sets
the sticky `PRACTICE` marker on the HUD for the rest of the session — it must
not be possible to teleport and then forget it happened while recording.
