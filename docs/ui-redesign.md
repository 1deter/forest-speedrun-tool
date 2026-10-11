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

Yellow on black, as the game's loading screen and the site (decisions.md
*Look: yellow on black*; T-0258). The colours live in `Data/UiPalette`, one
full set per theme; they are preset themes the runner picks in Settings ->
Theme (config `Window/Theme`, switched live; author, 2026-10-11: keep them
all, plus the redesign's first look and Catppuccin's dark flavours).
Customisable colours come after v1.0. Besides the four below: *Blue-grey
(first redesign)* - UiKit's colours before T-0258, `#17191F` / `#20232B`,
accent `#F5C417` - and *Catppuccin Mocha / Macchiato / Frappé* (mantle
window, base cards, surface edges, subtext hints, its yellow accent, peach
warn).

| Token | site | black | warm (default) | translucent |
|---|---|---|---|---|
| Window / panel | `#000000` | `#000000` | `#0E0E09` | `#000000` @ 78 % |
| Card / section header | `#0D0D0D` | `#121212` | `#18170F` | `#1A1A1A` @ 71 % |
| Hairline border | `#222222` | `#2A2A2A` | `#2C291D` | `#333333` @ 90 % |
| Text | `#E5C501` (yellow) | `#EAEAEA` | `#ECE6D4` | `#EAEAEA` |
| Dim / descriptions | `#8A7A1C` / `#8B7B1D` | `#8C8C8C` / `#C2C2C2` | `#9A927A` / `#C8C0A8` | `#9A9A9A` / `#C8C8C8` |
| **Accent** (all four) | `#E5C501` = rgb(229, 197, 1) | | | |
| Warn (changes the game) | `#FF8C33` (unchanged) | | | |

A switch repaints UiKit's textures in place and recolours every style made
with `UiKit.Style(src)` (modules use it instead of `new GUIStyle(src)`), so a
text colour in a variant must differ from its other text colours - a unit
test holds it. Semantic colours (split deltas, map markers, QA pass / fail)
are a module's own and stay.

| Layout | Value |
|---|---|
| Radius | 6 px (panels), 4 px (controls) |
| Spacing | 4 / 8 / 12 px; 8 px window padding |
| Type scale | 12 body, 11 dim / hints, 13 section title, 26 headline (results), widget value 16-48 by scale |

Rounded boxes are 9-slice textures generated once (`UiKit`, anti-aliased
corners) and kept alive with `HideAndDontSave`. A cloned `GUISkin` carries the
styles, so every existing tab inherits the look the moment its panel is drawn
with `GUI.skin = UiKit.Skin` (set around panel drawing only; the HUD and the
game's own GUI are untouched).

## HUD customiser

T-0018 (author, 2026-10-10; decisions.md *The HUD is a column of values*,
*Flags, not headings*):

- Every ticked value (`Data/HudLines`) shows. By default it sits in the
  **column** at the HUD position (Settings -> HUD; top left), in the
  pre-overhaul box's order, title first; no backing; a value not showing
  leaves no gap. Then the orange **flags**: what changes the game (short
  names, no "ON NOW" heading) and the practice tool used (its short name,
  nothing while clean). The *Info box* option and *own* toggle are gone.
- A value shows its **value only**, with the runner's own text before /
  after it (Edit HUD -> *Text*). The 100% totals keep their labels (four
  values on one line).
- **Edit mode** (Settings -> HUD -> *Edit HUD layout*, or the HUD button on
  the window's tab strip): the window (opened beside the column) lists the
  panels and values (show toggle, *Text*, and for a placed value size - / +
  and *To column*); the screen outlines the column, its grip and each
  placed value. Drag a value out of the column to place it; drag it to
  move, its corner to resize (0.5 - 6x); drop it on the column (it lights
  up), right-click it or press *To column* to put it back. The column moves
  by its grip (the bar beside it); with the window open but not editing,
  the whole column drags.
- Layout = part of the active HUD profile, `config/ForestOverlay/hud/<name>.txt`
  (`Data/HudProfile` + `Data/HudLayout`, tested; T-0019 - its settings are
  `@Section.Key = value` lines above these):
  ```
  # ForestOverlay HUD layout
  ShowSpeed: free, x=24, y=80, scale=2.5, after=" u/s"
  ShowPosition: before="Pos "
  ```
  Only changed values are listed; unknown tokens are ignored (`label=`
  from the first builds too), numbers clamped, text one line, 40 characters.
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
-- Attempts ---------------------------------------- (open, fills the rest)
> Checkpoint states     > Comparison sources (runner / LiveSplit)
> Run lines             > Ghost and replay            > Upload to the site
> TAS                   > Clear times
```
T-0257 (2026-10-10): no splits table in the tab - the splits and results
panels are overlays, switched on, set up, moved and resized in Edit HUD
mode (its *Panels* group); the runner name is under *Upload to the site*,
the PB / golds line on top of *Attempts*.
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

T-0226 (2026-10-08): a **Developer** tab (last: bridge, benchmarks,
experimental, TAS, memory census, dumps), Settings as one page of folds
(Keys, Info box, Performance, Loads and savestates), Debug views renamed
**Views**, the Deaths tab's duplicate toggle gone. Built + tests; not seen in
game yet (T-0025's QA).

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
8. **Toggles: a filled box, no check mark inside.** Since T-0021 (author,
   2026-10-11): a true 16x16 square centred on its row (`UiKit.Toggle`
   draws it - the toggle style had stretched it to the row height), an
   outline when off, a solid yellow square when on.
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
*Replaced by T-0022 (2026-10-11):* values render at the smallest of 32 /
64 / 96 px at or above their size and scale *down* (one 32 px render
scaled up to 4.5x looked choppy), drawn with their own Arial font instance
so their glyphs never share the UI's font texture.
Cursor bug steps from the author: Settings tab, F2 to close - "seems to be
fixed now though?"

**Later the same evening (all seen in game):** the yellow came out orange
(`#E28903`, every channel ~ value^2.27): the game treated UiKit's textures
as sRGB - they are now created linear and match the logo (`#F5C518`).
**Toasts** (`Ctx.Notice`): a UiKit card at the top middle, slides down /
back up (0.25 s, smoothstep), a 2 px yellow bar runs out over its time.
**Info box off by default** (`HUD.InfoBox`, Settings -> Info box, with a
tooltip): only widgets + the ON NOW / PRACTICE markers (honest labelling,
two plain lines at the box position) draw. **Run mode text**: "Run mode:
off" / "ON - attempt n", the explanations in tooltips; "Send attempts to the
website" + tooltip.
Left on the list: the rest of item 9 across the other tabs (Practice,
Deaths, Debug views, Settings bodies still have paragraphs); widgets for
the update line (a toast when an update is found would fit); the cursor
check; snapping; saved section state.
**Then (seen in game):** ON NOW is a heading + one feature per line (cheat
menu "enabled" list); the markers drag like the box did; speed shows the
number only; the results panel uses one precision everywhere (the finer of
the time / delta decimal settings - an 11.331 PB said "by 0.03 (was 11.37)").
**Then:** widgets transparent with a 2 px shadow (card only in edit mode;
the shadow dropped by T-0022, author 2026-10-11: plain text);
splits panel / run code box / drag outlines on UiKit; results gold = the
accent; a toast when an update is out; `UiText.Note` turns an explanation
under a control into that row's tooltip (13 notes in Settings, Practice,
Inventory, Deaths - statuses stay `DrawDim`); the window panel opaque.
Left: the remaining `DrawDim` explanations (Practice `FullLoadHint` /
`QuickLoadHint`, Debug views, Map, 100%), then merge + release (the
author's call).
**2026-10-08: main merged in; the cursor (item 3) fixed** (T-0024): the
author's steps were F2, Esc, Settings, F2 - the pause menu opened while the
window was up, and the close re-locked the mouse and released the player
lock under the menu. `CursorController.Release` and
`ModuleHost.ReleasePlayerLock` now leave both to the game while the pause
menu is open (author confirmed in game).
