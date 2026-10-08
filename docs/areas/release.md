# Area: releases and updates

How a change reaches runners: the version, the changelog, the tag, CI, the
in-game updater and the patcher. Read it before a release or any change to
`Core/UpdateChecker`, `Core/UpdaterInstaller`, `Data/UpdateStaging` or
`patcher/`.

## Releasing

After each plugin change that builds and passes tests; docs-only changes
need no version or tag. The steps: skill `release`
(`.claude/skills/release/`). `python scripts/bump.py 0.24.N "bullet"
"bullet"` sets the csproj `Version`/`AssemblyVersion`/`FileVersion`,
`Plugin.PluginVersion` and the `CHANGELOG.md` section (`-f notes.md` for
longer notes); it does not commit, tag or push.

- **Changelog** (author, 2026-09-23, "all future updates"): `CHANGELOG.md`,
  one runner-facing section per release. CI puts the tag's section in the
  GitHub release and fails without one; the Updates tab shows the latest
  release's notes ("What's new in vX", "(installed)").

The author runs the latest release via the in-game updater (Slot 1).

## Never deploy by hand

Deploy fails with "user-mapped section open" if the game is running. **Do not
deploy into the author's install unasked** — it now updates through the real
release path (see *How updates work* below), and a hand-copied DLL hides whether
that path works.

## How updates work

**The plugin DLL is the whole install.** It carries the shipped data files
and the update patcher as embedded resources and writes both out on startup.

```
tag vX.Y.Z -> CI builds + tests -> GitHub Release with ForestOverlay.dll
  -> in game: startup check, Updates tab -> Download
  -> ForestOverlay.dll.pending beside the plugin
  -> next launch: ForestOverlay.Updater (BepInEx/patchers) runs before plugins,
     moves .pending into place, keeps the old DLL as .bak
```

- **Version lives in two places** — `ForestOverlay.csproj` and
  `Plugin.PluginVersion`. They must match; the updater compares against the
  latter.
- **Every release has a `CHANGELOG.md` section** (author, 2026-09-23: "for
  all future updates"). `## vX.Y.Z - date`, a few short runner-facing
  bullets. CI copies the tag's section into the GitHub release body
  (`body_path`) and **fails the release if the section is missing**; the
  plugin's Updates tab reads the body from the release JSON it already
  fetches (`ReleaseJson.ExtractNotes`) and shows it - "What's new in vX"
  for an update, "(installed)" once it is the running version. Write it in
  plain words; hard-wrapped lines are joined in game.
- **A tag publishes before its DLL is attached.** Wait for the asset, not the
  release, before telling anyone to update. The plugin reads a release with no
  DLL as "still being published" and re-checks every minute.
- **An attached DLL can still 404 for a while.** On v0.19.1 the API listed the
  asset as `uploaded` while the runner's download got GitHub's 9-byte
  `Not Found` (Unity 5.6's `UnityWebRequest` does not flag a 404), even though
  a curl from here already got 200. Since v0.19.2 the updater treats a 404 or
  a tiny non-DLL body as "not downloadable yet" and retries every 30 s. Anyone
  on 0.19.1 or older who hits it just clicks Download again later.
- **Never poll `api.github.com` to watch a release.** Anonymous API calls are
  limited to 60 an hour *per IP*, shared with the author's own game — polling
  once locked their in-game update check out for an hour. Poll the asset
  instead; downloads are not API calls:
  `curl -s -o /dev/null -w '%{http_code}' -L https://github.com/1deter/forest-speedrun-tool/releases/download/vX.Y.Z/ForestOverlay.dll`
  (200 = attached).
- **A runner's data all lives in `BepInEx/config/`** (`ForestOverlay/segments/my-segments.txt`,
  older `locations/my-spots.txt`, `savestates/`, `runs/`, plus
  `com.deter.forestoverlay.cfg`): moving or replacing the BepInEx folder
  takes it along (maks, 2026-09-25, lost his spots that way) - copy
  `config/ForestOverlay` back with the game closed.
- **Rollback:** close the game, delete `ForestOverlay.dll`, rename
  `ForestOverlay.dll.bak` to `ForestOverlay.dll`. A download that is not the
  ForestOverlay assembly is renamed `.rejected` and never installed; if
  that leaves no `ForestOverlay.dll`, the patcher puts the `.bak` back.
- **Any plugin file name (v0.23.7).** The download is always staged as
  `ForestOverlay.dll.pending` beside the running plugin
  (`Data/UpdateStaging`); a plugin running as e.g. `ForestOverlay(1).dll`
  renames itself to `ForestOverlay.dll.bak` at download time (a loaded
  DLL can be renamed on Windows), so the restart leaves one
  `ForestOverlay.dll`. At startup `UpdateChecker.TidyPluginFolder`
  deletes a stale `<other name>.dll.pending` and renames any second
  ForestOverlay assembly in the folder to `.old`. Installs older than
  v0.23.7 under another name need one manual rename - their code stages
  the wrong name.
- **The patcher updates less reliably than the plugin** — it is loaded while
  the game runs, so `Core/UpdaterInstaller` swaps it by renaming the loaded
  copy aside. Keep `patcher/` small and its behaviour stable.
- **Manual test without a release:** save any ForestOverlay.dll as
  `BepInEx/plugins/ForestOverlay.dll.pending` and launch.
- **Confirmed end to end in game (v0.16.2 -> v0.16.3):** check, Download,
  restart, installed, `.bak` kept — and the plugin replaced the loaded patcher
  by renaming it aside.

## Known issues for runners

- **A renamed plugin** (`ForestOverlay(1).dll`) on a version older than
  v0.23.7 never updates - the runner renames it to `ForestOverlay.dll`
  once, game closed.
- **Installs older than v0.16.2 cannot download updates**; older than
  v0.19.2 can hit the post-release 404 (click Download again later).
- `gh` is not installed on this machine: release pages cannot be edited
  from here. CI writes every release's notes from `CHANGELOG.md`.

## Gotchas

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to, ending with its marker: `[check: <lint / test>]`, `[check: T-n]` (the task building it) or `[judgement]` (`lint.py` checks it; T-0009).

10. **What only `deploy.ps1` copies is missing for runners** - ship data inside the DLL. [check: lint.py check_deploy]
15. **Unity 5.6's `UnityWebRequest` ignores 404** - check `responseCode` yourself. [check: lint.py check_web_requests]
44. **Measure before the changelog claims a number.** [check: stop.py changelog number]
65. **A release chain must stop when a step fails** - join a script edit to the bump with `&&` (v0.24.172 shipped empty). [check: lint.py versions + pre-push]
92. **The plugin's SDK project compiles every `.cs` under the repo** - a new top-level project folder goes into `ForestOverlay.csproj`'s `Remove` lines in the same commit; build the plugin before pushing. [check: lint.py removes]
