# UI redesign (draft, branch `ui-redesign`)

Author, 2026-10-05: "it's 2026 and i don't want the ui to look crap, i want it
to be very UX friendly because it already has a lot of functionality that's
going to be overwhelming when runners first interact with it."

## Research, in short

- **Momentum Mod 0.10.8 (2026-08-04) HUD Customizer**: an edit mode entered
  from an icon; every HUD component can be moved, resized, restyled; layout
  is a file; drag-and-drop with snapping, stack groups, an auto-generated
  settings UI per component
  ([issue #2158](https://github.com/momentum-mod/game/issues/2158),
  [changelog](https://blog.momentum-mod.org/posts/changelog/0.10.8/)).
  Take: **one widget = one value**, edit mode shows outlines + handles, the
  layout is data.
- **LiveSplit layouts**: components (timer, splits, delta, previous segment)
  each with their own font / size / colour, stacked in one box, saved as a
  `.lsl`. Take: the big readable timer is the thing runners look at; the
  rest is secondary and small. Speedrun HUD convention: monospace-looking
  digits, colour only for meaning (ahead green / behind red / gold).
- **Progressive disclosure** (accordions, tooltip-on-demand, "make it clear
  where advanced functions are"): essentials open, advanced collapsed behind
  a labelled header, explanations on hover instead of paragraphs
  ([IxDF](https://ixdf.org/literature/topics/progressive-disclosure)).

## Principles

1. **Essentials first, the rest one click away.** Each tab opens with what a
   new runner needs; everything else is a collapsed section with a name that
   says what is inside.
2. **Say it once, short, where it is used.** A control has a short label; the
   why / how is a hover tooltip (`UiKit.Hint`). No paragraphs in the body.
3. **One accent colour**, used for the primary action, the active tab, the
   open section marker and on-state toggles. Colour otherwise only means
   something (green ahead, red behind, gold PB, orange = changes the game).
4. **Honest labelling stays**: PRACTICE marker, ON NOW, run code are not
   restyled away.
5. **Nothing changes until the runner edits**: HUD defaults reproduce today's
   info box.
6. **Never allocate in OnGUI**: textures / styles / skin built once,
   tooltips are cached `GUIContent`s.

## Visual language

| Token | Value |
|---|---|
| Window / panel | `#17191F` @ 96 % |
| Card / section header | `#20232B` |
| Hairline border | `#2E323C` |
| Text / dim text | `#E6E8EB` / `#9AA0AA` |
| **Accent** (forest green) | `#4CC790` |
| Warn (changes the game) | `#FF8C33` (unchanged) |
| Radius | 6 px (panels), 4 px (controls) |
| Spacing | 4 / 8 / 12 px; 8 px window padding |
| Type scale | 12 body, 11 dim / hints, 13 section title, 26 headline (results), widget value 16-48 by scale |

Rounded boxes are 9-slice textures generated once (`UiKit`, anti-aliased
corners) and kept alive with `HideAndDontSave`. A cloned `GUISkin` carries the
styles, so every existing tab inherits the look the moment its panel is drawn
with `GUI.skin = UiKit.Skin` (set around panel drawing only; the HUD and the
game's own GUI are untouched).

## HUD customiser

- Every info-box line that has a switch (`Data/HudLines`) is a **widget**
  with a key (its config key, e.g. `ShowSpeed`). Default: it lives inside the
  info box as today.
- **Edit mode** (Settings -> Info box -> *Edit layout*, or the HUD button on
  the window's tab strip): the window collapses to a widget list (show
  toggle, "own widget" toggle, size - / +, reset) and the screen shows an
  outline on every widget and on the box. Drag a widget out of the box to
  make it its own widget; drag its corner handle to resize (font scale
  0.5 - 6x); right-click to put it back; drag the box to move it.
- A free widget draws as a card: dim label above, the value big, in the
  results panel's look (rounded dark card, optional). Label can be hidden.
- Layout = `config/ForestOverlay/hud-layout.txt` (`Data/HudLayout`, tested):
  ```
  # ForestOverlay HUD layout
  ShowSpeed: free, x=24, y=80, scale=2.5, label=off
  ShowRunTimer: free, x=700, y=40, scale=3
  ```
  Only free widgets are listed; unknown tokens are ignored, numbers clamped.
- The run timer as a widget replaces the old black box with yellow text:
  the splits panel also steps aside while the results panel is up (they
  overlapped on small screens).

## Per-tab layout

**Window chrome**: rounded window, title row (name + version, accent dot
when an update is out), tab strip as pills (active = accent underline), body
8 px padded, a hover tooltip layer drawn last. Footer-less.

**Runs** (was a 30-control wall):
```
[x] Practice mode        Segment: Cave 5 -> Cave 6        (header, always)
[ Restart ] [ Split / finish ] [ Abort ]      Compare to: [best v]
-- Run mode ---------------------------------------- (open)
-- Splits table ------------------------------------ (open)
-- Attempts ---------------------------------------- (open, fills the rest)
> Checkpoint states     > Comparison sources (runner / LiveSplit)
> Run lines             > Ghost and replay            > Upload to the site
> Splits panel options  > TAS                          > Clear times
```
Collapsed headers show a one-line summary on the right (e.g. "off",
"3 states"). Descriptions move into tooltips.

**Practice, Deaths, Debug views, Inventory, Settings** (next): same header /
section / tooltip kit; Settings first splits into *Essentials* (hotkeys that
matter, HUD, run mode) and *Advanced* (performance patches, diagnostics,
bridge).

**Onboarding**: first launch opens the window on Practice with a three-line
"Start here" card (Save spot F6, Restart F7, Runs tab) that is dismissed once.

## Status of this branch

Built and unit-tested (915 tests); **never run in the game** (no visuals checked -
the skin's slider / scrollbar are deliberately left default).

Done: `Core/UiKit` (palette, generated rounded textures, cloned skin applied
around panel drawing, collapsible `Section`, `Hint` tooltip), window chrome
and tab pills, `Data/HudLayout` (+ tests), `Core/HudWidgets` (free widgets,
edit mode, editor list in the window), Settings -> Edit HUD layout, the
window's *Edit HUD* button, Runs tab sections + tooltips, splits panel steps
aside while the results panel shows, both in the card style.

Left: other tabs (Practice, Deaths, Debug views, Inventory, 100%, QA,
Updates) still use the old layouts under the new skin; onboarding card;
section open / closed state is not persisted; widgets are not snapped / no
stack groups; the run timer widget has no delta colouring yet; verify
checkbox / window textures and tooltip placement in game.

## First in-game try (author, 2026-10-05)

- **Title over the tab pills** - fixed: the window style drew the title
  inside `padding.top` (28) at y 34, on the tab strip (y 26); `contentOffset`
  y -22 puts it in the header band. Checked in game (shot after a Slot 1 load).
- **One native crash loading a save** (`d3d11.dll` access violation, crash
  folder `<game>/2026-10-05_120832`). Not reproduced: two Slot 1 loads over the
  bridge with the draft (window opened before) were fine. Every texture the
  draft draws is its own, `HideAndDontSave`; that session also changed
  resolution (1366x768 -> 2560x1664) before the load, and the author says the
  PC has been acting up. Watch for a second one before blaming the draft.

The author is running the draft build (their v0.24.248 is
`BepInEx/plugins/ForestOverlay.dll.mine`).

## Author's verdict after trying it (2026-10-05) - the work list

"The concept of the overhaul is not bad, it just needs some more work."
In the author's words where it matters; do these before anything else.

1. **Accent: The Forest's yellow, not green** - black on the logo's yellow
   (sample the logo / key art; roughly `#F2C21B`, confirm against the game's
   own UI). Replaces `#4CC790` everywhere.
2. **Attempts section leaves dead padding** under "No attempts yet", and the
   scroll wheel does nothing while the mouse is over that region. A section
   takes the height of its content; the whole body scrolls everywhere.
3. **Cursor lost** after closing F2 while on the Settings tab - the same bug
   as the early ESC-menu one, now in another pane. Reproduce (Settings tab,
   F2 close), read `CursorController` / `GameInput` hand-back.
4. **Remove the top-left info box** (or refactor it away): the HUD customiser
   replaces it. On-screen UI is **minimal but informative**. The update line,
   ON NOW, PRACTICE etc. go elsewhere, each on its own.
5. **No drag-out-of-the-box magic**: dragging the box popped every line out
   as the mouse passed over them - "over-engineering simplicity". Widgets are
   added / placed explicitly; the box is not a container that sheds them.
6. **A widget shows its value only - no title** ("runners should know what
   they put on their screen").
7. **One function per display**: speed's "(tot x)" is two things in one -
   make them separate widgets / modes (Momentum Mod's speedometer: one
   mode per widget). Same rule everywhere (author's design doc).
8. **Toggles: a filled box, no check mark inside.**
9. **Feature = name + toggle; the rest on hover** (brief description). No
   paragraphs on the page (the Runs tab's run mode text is the example).
10. **Notifications as toasts**: slide in / out smoothly, a thin progress bar
    depleting while it shows. Aesthetic reference: polished cheat-menu UIs
    (look, not function). Small UX niceties over words on the page.

More will come as they test.

### Progress on the list (2026-10-05, same evening)

Done (in game: yellow + filled toggles + value-only speed widget seen in a
shot): 1 yellow accent (`#F2C21B`, black text on it), 2 Attempts as tall as
its rows (max 320 px, scrolls inside after that), 5 drag-out removed
(`BoxLineEvent` is a no-op; the editor list's "own" toggle makes a widget),
6 no widget titles (the label toggle is gone; `label=` in old layout files is
ignored when drawing), 7 `Total speed` is its own line / widget
(`ShowTotalSpeed`, off by default), 8 no check mark.
Not reproduced: 3 (cursor after closing F2 in Settings - the bridge's close
leaves the same cursor state from Settings as from Runs; the author's F2
path differs - ask for the exact steps). Left: 3, 4 (info box out), 9 (cut
the run mode paragraph and other body text to tooltips), 10 (toasts).
Seen in the shot: text vanished where the window sat over a bright white
scene (Inventory tab label, parts of rows) - check the panel's alpha /
whether the window draws in two passes.

**Flickering letters (author's video, 2026-10-05)** - letters vanished
across the whole UI (and the author saw glitches in game too). Cause taken
as Unity's shared dynamic-font texture thrashing: widgets drew values at a
font size per scale (up to 96 px bold). Fix: values render at one size
(32 px) scaled by `GUI.matrix`. Two shots after the fix: text intact. If it
comes back, count the font sizes everything draws at (UiKit: 11-13, 26).
Cursor bug steps from the author: Settings tab, F2 to close - "seems to be
fixed now though?"
