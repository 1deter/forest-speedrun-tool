# Area: the website (`site/`, forest.deter.cloud)

ASP.NET Core (.NET 10) + SQLite + plain HTML / JS on the author's VPS,
linking the plugin's pure `src/Data` format files. The rules every site
change follows are in [`site/CLAUDE.md`](../../site/CLAUDE.md) (loaded by
itself when you open a file under `site/`); the site's decisions in
[`docs/decisions.md`](../decisions.md) *Site*; open work:
`python scripts/tasks.py list --open --area site`.

## What it is for

- **Shared runs and a web viewer** (runner request): everyone's runs of a
  spot against yours, in the style of Momentum Mod - lines, ghosts, splits.
- **Leaderboards are comparative, not competitive** (docs/decisions.md
  *Conventions*): lines and ghosts, no verified ranking, so no anti-cheat.
  Deleting a run and flagging a time faster than the golds is enough.
- Later: 3D terrain from the heightmap, caves (need a geometry dump), a
  scrub bar, annotations.

## Where we are

Spots, runs uploaded from the game (on by default; a test run that
finishes uploads - delete it, docs/bridge.md *Test spots*), other
runners' PBs as comparisons, the admin page, spot submissions, the photo
map (recaptured 2026-10-03 on v0.24.222) and the 3D world of the game's
own models (surface, caves, the endgame lab; per kind switches, texture
packs).

## Commands

```bash
dotnet test site/ForestSite.Tests      # stop a running forest-site preview first (it locks ForestSite.exe)
```

- Run it: the preview `forest-site` (`.claude/launch.json`,
  `http://localhost:5080`, owner token `local-admin`); an agent in its own
  worktree runs `dotnet run --project site/ForestSite --urls http://localhost:5083`.
- Deploy: every push to `main` touching `site/`, `src/Data/`,
  `community/` deploys (`.github/workflows/site.yml` -> `site/deploy/deploy.sh`;
  one-time setup [`site/deploy/README.md`](../../site/deploy/README.md)).
  Watch a deploy by polling the live page with a **new query string each
  poll** (Cloudflare caches a `?v=` URL), never `api.github.com`.

## The formats (all text, all already written by the plugin)

- **`.foseg`** - one segment: `Data/SegmentBundle` (header, `[segment]`,
  optional `[startstate]`, `[attempt]` sections). Tested round trip.
- **`[segment]` block** - `Data/SegmentFormat`: id, name, category, spawn,
  start / check / end triggers (`Data/Segments.cs`, `TriggerParser`),
  `split = <name>` after a check / end line (v0.24.146), restore,
  startstate hash, notes.
- **`.run` (an attempt)** - `Data/AttemptFormat` (pure, tested): `anchor`,
  `recorded`, `duration`, `route`, `splits|t1|t2...` (checkpoint times,
  cumulative, ms; the end is `duration`), `runner|<id>|<name>`,
  `channels|...`, `s|t|x|y|z|speed` (30 Hz path), `v|t|...` (5 Hz state).
  Files before v0.24.146 have no `splits` / `runner` lines.
- **Splits maths** - `Data/SplitTable` (PB, golds, sum of best, average,
  deltas, LiveSplit colours). The site should reproduce the same numbers;
  porting this file (or its tests) keeps both sides honest.
- Coordinates are Unity world metres (y up); the surface map spans about
  -1750..1750 on x / z; caves are below the terrain (y < 0 in places).

## Local work

**The local preview versions its assets at startup** (`app.js?v=<hash>`,
computed once): after editing `wwwroot`, restart the `forest-site`
preview or the page keeps the old script and CSS (a deploy restarts, so
live is fine). Test runs for the local site: post a `.foseg` with a
registered local runner (`/api/register`, then `/api/runs` with the
token); an attempt's `runner|` line must be that runner's, or it is
refused as "another runner's attempt".

**Testing the plugin against a local site** (preview `forest-site`,
`http://localhost:5080`, owner token `local-admin` from
`.claude/launch.json`): `set ..._modules[16]._url.Value
http://localhost:5080`, and back to `https://forest.deter.cloud` after.
The author's config holds a **live token** now: leave it in place (the
local site answers 401 - enough to test a request path; a session may
not copy the token aside) or, to test a full registration locally, clear
it and have the author **Reset token** for their runner on the live
`/admin` afterwards, so the plugin registers again live. A local test
that sets `_tokenBad` (401) needs `set ..._modules[16]._tokenBad false`.
Pressing Submit over the bridge: `call ..._modules[9].SubmitSelected`
(after `QuickSaveSpot`, which selects the new spot; remove it after per
*Removing test spots*).

## The reference: [`docs/website.md`](../website.md)

Read the section for what you change:

| Section | Read it when |
|---|---|
| *What is built* | Endpoints, the admin page, clean paths, admins and tokens |
| *How spots get onto the site* | Owners, community spots, submissions |
| *The photo map* | The aerial capture / bake / upload pipeline |
| *The 3D world* | Anything in `map3d.js`, the model / texture exports |
| *Caves on the 2D map* | `cave-bake.py`, underground drawing |
| *Spot categories ...*, *Compare*, *Buildings ...*, *Item list* | Those features |
| *Next* | Detail behind the site's tasks |
| *Security* + *Audit 2026-10-04* | Any endpoint, auth, CSP or upload change |

## Gotchas

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to.

63. **An image read at the same path can be the old picture** - name every test shot uniquely before comparing.
64. **A Unity PPtr's file id is relative to the file holding it** - resolve through the referencing file's externals (`ref_key`).
66. **Uploaded files cached for a day need a version in their URL** - build stamp in the json, `?v=` on every file.
67. **Scene files hold placeholders, not the world** - LOD-spawned trees / rocks / cave walls need the in-game dump; a look can come from more than `_MainTex`.
68. **Texture size = UVs x the material's tiling** - export `m_Scale`; render the local site at the spot and compare with a game `shot` before calling a render fix done.
69. **A subclass can override the spawn's scale** - `LOD_Cave.SetLOD` scales the piece like its placeholder; read every override (`ilscan refs set_localScale`) and check a spawned object live against its placeholder.
70. **An object's origin is not where its mesh is** - cave grounds / mountains sit at 0,0,0 with world-space vertices; chunk by the mesh's bounds. For a hole, `call static:UnityEngine.Physics OverlapSphere x,y,z r` names what is there.
71. **A `?v=` the server ignores protects nothing** - index-named files + a page holding the old json = a mixed world (Cave 6's "leaves"); the server refuses another build. Reproduce on a fresh load before blaming the data; ask how long the page was open.
72. **A check per row is not a check per thing** - a spot is many routes and shows the newest one's labels: a new row could rename it. Ask who can create the row that wins, not only who can edit one.
73. **Diff a switch's two outputs before shipping it** - the "-dry" photo layer was the wet one (the ocean never draws in the capture) and "eye adaptation off" did not hold; compare on / off results and read a setting back before building on it.
74. **A check against a clamped result must clamp its input too** - the 3D patch, clamped inside the map, never "covered" a centre near the edge and was rebuilt every 0.4 s (the white flicker).
75. **Switch layers off before fixing what a symptom looks like** - the "lakes over land" were the sea plane in inland pits, not the lake models; hide models / sea / patch in turn, and a raycast that hits nothing is not a model. Corrected 2026-10-03: those pits ARE water in game (only the sinkhole is dry) - check a "dry" verdict in game.
76. **A game can have more than one distance switch** - LOD_Manager's ranges and 963 `LOD_GroupToggle`s with their own; a shape cut at a tile edge = a switch on the tile's centre (`ilscan refs PlayerCamLocation::PlayerLoc`).
77. **A scene object can be moved at run time** - the yacht stands 130 m from its scene position (a positive handle under a spawned root); check an exported object against `find` before chasing its look.
78. **A folder read whole turns a diagnostic dump into data** - test `placed-*.txt` dumps went into the export (and the diff matched them against themselves); keep them out, check against a clean input; key "the game lists it" on paths, not places.
83. **A picture can depend on load order** - a diff that bisects to something unrelated: rerun the old build with delayed files (`site-measure.py DELAY`) before blaming the change.
85. **Count what a batch would merge before building it** - BatchedMesh by material was slower: ANGLE's multi-draw is a loop (an item ~ a draw call) and 623 distinct textures meant almost nothing shared a material.
86. **A shadow with no object: the object is behind the camera's clip** - the capture camera sat by the terrain inside the south mountains' models; place it by the tallest renderer.
88. **An image library's resize can read the alpha as coverage** - Pillow's RGBA thumbnail premultiplies; Standard textures keep smoothness there (0) and 19 lab textures exported black. Resize colour and alpha apart; count black textures after an export.
