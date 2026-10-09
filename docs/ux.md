# UX framework and review checklist

The one design framework for everything a runner sees: the plugin's
window and HUD, forest.deter.cloud, and the knowledge bot's Discord
messages. `forest-ux` (`.claude/agents/forest-ux.md`, T-0252) reviews
against it and keeps it current; anyone building UI reads it first.

**The goal** (author, 2026-10-09): a tool that looks good and is easy to
learn without flooding a new runner. Less on screen and fewer clicks is
faster to learn. The author's rules are the floor; the researched
guidance below fills in what they do not cover.

Where the other facts live (one home each):
- The author's UI rules: `docs/decisions.md` *Plugin* (*Easy to learn,
  little at once*, naming) and memory `ui-design-preferences`; the
  module rules (`UiText`, vertical layout, show why on screen) in
  `src/CLAUDE.md`.
- The plugin's visual tokens (palette, radius, spacing, type scale):
  `src/Core/UiKit.cs` and `docs/ui-redesign.md`, both on the
  `ui-redesign` branch until T-0025 merges it. The look is yellow on
  black, like the original game (`docs/decisions.md` *Plugin: Look*):
  anything else on screen is a finding.
- The site's tokens: `:root` in `site/ForestSite/wwwroot/style.css`.
- Runner-facing words: `knowledge/glossary.md`; UI names fixed by the
  author (Quick load, Full load, Reload save on death, spot, segment):
  `docs/decisions.md` *Plugin: Naming*.

## The checks

Each check is a question with a yes / no answer on what is on screen.
A "no" is a finding. The source is in brackets.

### A. Little at once (progressive disclosure)

1. Does the first view of each tab show only what most runners use
   most often, the rest one labelled click away? [NN/g progressive
   disclosure; author: advanced as a per-tab toggle]
2. Two levels at most: common, then advanced. Is anything three deep
   (a fold inside the advanced view inside a sub-tab)? [NN/g: beyond two
   levels "typically" fails]
3. Does the control that opens the advanced view sit in a visible,
   predictable spot with a label that says what is behind it? [NN/g:
   information scent]
4. Is the advanced view organised in its own right, not the old long
   list back? [author, 2026-10-09]
5. Is there exactly one way to reach each option (or each route clearly
   labelled)? [NN/g; author: one journey, one place]
6. Does a task that spans tabs (start a timed segment: spot, state,
   practice mode, arm) finish in one place? Count the tabs and clicks.
   Baseline 2026-10-10 (T-0256): 5 clicks, 2 tabs, practice mode off
   (the default). [author: one journey, one place]

### B. Say it once, short, where it is used

7. Is each feature its name + a toggle, the description on hover, no
   paragraph on the page? [author, 2026-10-05]
8. Is anything needed to finish a task only in a tooltip (a rule, a
   required order, a key)? Tooltips vanish - that belongs on the page.
   [NN/g tooltips]
9. Does a tooltip add something its label does not already say? An
   empty-calorie tooltip is a finding. [NN/g tooltips]
10. Are instructions a runner must read and apply turned into a picture
    on the thing itself (a legend, a marker)? [author: pictures over
    instructions]

### C. Status, feedback, errors

11. Does every click show a result within ~0.4 s, under the control
    that caused it? [Nielsen 1; Doherty threshold; author: messages
    where the click was]
12. Can a runner tell what state everything is in without opening
    anything (armed, practice, recording, uploads on)? [Nielsen 1;
    honest labelling stays]
13. If something does nothing (dead toggle, empty list, a timer that
    will not start), does the screen say why? [`src/CLAUDE.md`]
14. Does an error say what happened and what to do, in runner words?
    [Nielsen 9]
15. Can anything that creates, overwrites or deletes happen by
    accident? It needs its own button and a confirm or undo when it
    destroys; everything else is automatic. [Nielsen 3, 5; author:
    automatic unless it can lose work]

### D. Consistency

16. Same thing, same word everywhere - plugin, site, bot, the glossary?
    [Nielsen 4; Jakob's law]
17. Same control, same look and place on every tab (the primary action,
    the advanced toggle, close)? [Nielsen 4]
18. Is colour only ever meaning (ahead green, behind red, gold best,
    orange changes the game, the accent for the primary / active), and
    never the only signal? [LiveSplit convention; WCAG 1.4.1]
19. Are settings functionally separate - one setting, one feature; no
    hidden setting that still does something? [author, 2026-10-09]
20. One button, one job? [author, 2026-09-23]

### E. Recognition over recall

21. Can a runner use the tab without remembering anything from another
    tab (an id, a key, which spot is armed)? [Nielsen 6]
22. Are hotkeys shown next to the action they trigger? [Nielsen 6, 7]
23. Fewer, clearer choices: does any single view offer more than ~7
    peer choices without grouping? [Hick's law; Miller as a rough
    guide, not a limit]

### F. Readable and reachable

24. No clipped text at the smallest supported window (1366 x 768) and
    at UI scale extremes - every scrolled frame, not the first view.
    [author; `UiText`]
25. Is body text large enough to read at a glance? XAG 101 sets 18 px
    body height (lowest descender to highest ascender) at 1080p for PC;
    measure on a screenshot and report the number - the size itself is
    the author's call. [Xbox Accessibility Guidelines 101]
26. Lines of text under ~80 characters; no all-caps sentences. [XAG 101]
27. Text contrast at least 4.5:1 (3:1 for large text and for control
    edges) against what is behind it - the game shows through panels.
    [WCAG 1.4.3 / 1.4.11]
28. Are frequent targets big and near where the eye already is; nothing
    that destroys sits next to something used often? [Fitts's law]
29. Related things grouped by proximity or a shared box, unrelated
    things apart? [Gestalt: proximity, common region]
30. Does the HUD show values only, one function per widget, with the
    timer the most readable thing? [author, 2026-10-05; LiveSplit]

### G. First run

Play a new runner who has never opened the tool (a fresh config if the
review needs it - back it up, rule 15):
31. On first open, is it obvious what to do first?
32. Can they, in two clicks or fewer from the window: practise a spot,
    start a timed run, read their last result, change a hotkey, find
    the update? Record the click count for each; one over two is a
    finding.
33. Is there anything on the first screen a new runner does not need
    yet?

### H. The site (forest.deter.cloud)

The same checks A-F on each page at 1366 px and at phone width (375 px,
no sideways scroll), plus:
34. Does every page answer "where am I and how do I get back"? [Nielsen
    1, 3]
35. Do loading and empty states say what is coming / why it is empty?

### I. The bot's Discord messages

36. Does the answer lead with the answer, then the detail? A runner
    reads the first two lines in a busy channel.
37. Discord markdown only where it renders (bold, lists, headers up to
    `###`, code); no tables (Discord shows them as raw pipes); under
    2000 characters a message.
38. Same words as the plugin and the site (check 16), sources as
    links, not pasted URLs in prose.

## Severity

[Nielsen's severity scale]: **4** blocks a task or loses work; **3** a
runner gets it wrong or gives up often; **2** slows them or confuses
once; **1** cosmetic; **0** not a problem. A finding carries its number
and becomes the task's priority: 4 -> P1, 3 -> P2, 2 -> P3, 1 -> P4.

## How a review runs

`forest-ux` (Sonnet; Opus for a first full audit, set at spawn):
1. Reads this file, the decisions it points to, and the task under
   review (`tasks.py brief T-n`) when there is one.
2. Tours every tab the change touches (a full audit: every tab) with
   `open_tab` + `scroll_tour`, each sub-view too (switch it on with
   `set`, e.g. Settings' `_hudView`, then tour again). A view named
   "not drawn now" is a sub-view to open, never one to skip.
3. Reads the drawing code of what it saw, for what the screen hides
   (states it did not reach, strings longer than the test data).
4. Site: the local preview (`.claude/launch.json` "forest-site") at both
   widths. Bot: the answers in `knowledge/eval/` runs or the answer log.
5. Files each finding as a task (`--source forest-ux:<date>`), with the
   check number, the severity, and the screenshot; a design choice (what
   to cut, a new word, a layout) is parked for the author with
   `--needs author-decision --question`. It never edits code.
   Method notes (2026-10-09): measure text height on the PNG the shot
   printed (the file on disk is the full frame; `region` crops only the
   returned image) - rows of bright pixels, ascender to descender, then
   scale to 1080p for check 25; read every `set` flag back at the end
   (`fields` on the module); `Hotkeys.Bindings.Count` and `HudLines.All`
   give the true list sizes for check 23.
6. Adds what it learned to this file (a new check, a sharper one, a
   section for a new surface) and a line to the log below.

## Sources

- NN/g, [Progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/)
- NN/g, [10 usability heuristics](https://www.nngroup.com/articles/ten-usability-heuristics/)
- NN/g, [Tooltip guidelines](https://www.nngroup.com/articles/tooltip-guidelines/)
- NN/g, [Severity ratings](https://www.nngroup.com/articles/how-to-rate-the-severity-of-usability-problems/)
- [Laws of UX](https://lawsofux.com/) (Hick, Fitts, Miller, Jakob, proximity, common region, Doherty)
- Xbox Accessibility Guidelines, [XAG 101 text display](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101)
- W3C, [WCAG 2.2](https://www.w3.org/TR/WCAG22/) (1.4.1 use of colour, 1.4.3 / 1.4.11 contrast)
- Momentum Mod and LiveSplit (HUD conventions): `docs/ui-redesign.md` *Research*

## Review log

- 2026-10-09 - written (T-0252); no review run yet.
- 2026-10-09 - smoke run: plugin Settings tab on v0.24.260, Keys and Info box (HUD) views, checks A-F; 6 tasks (T-0259..T-0264, 2 parked), notes on T-0226 and T-0021. First review: the brief's `open_tab` schema listed no `scroll_tour` but it worked.
- 2026-10-10 - T-0256 design proposal (not a review): Practice + Runs flow read on ui-redesign, toured on v0.24.267; 3 options in tasks/notes/T-0256.md, parked; 0 findings filed. Click baseline for check 6 / 32 added.
