# ForestOverlay: architecture and codebase review

Reviewed at `c9cb3ed` (plugin v0.24.401), 2026-11-17. Static review only: nothing was built, run or changed. Claims are from reading the code unless marked **[in-game]** (needs a check in the game) or **[unverified]**.

Size at this commit: plugin `src/` about 99,000 lines of C# (`Game/` 38,100, `Modules/` 27,100, `Data/` 26,000, `Core/` 6,500), tests 21,900, site 13,200, bot 5,100, Python harness 15,800, docs 18,900. 26 registered modules, about 40 classes that install Harmony patches, 117 config entries, 413 changelog sections.

## Summary

- **The code is careful line by line; the problem is scope.** It is a game-research workbench (in-process Mono profiler, memory census, type explorer, world and terrain dumper, FSM exporter, file-driven reflection RPC, TAS input injection, paint tool) shipped to every runner inside the same DLL as the overlay and the verification. I estimate a race-ready core at 15,000 to 20,000 lines, a fifth of today's.
- **Verification is not anti-cheat, and the code already knows it** (`docs/run-mode.md` *The honest limit*). It gives a tamper-evident, server-timestamped log for an unmodified client. A fork with a few lines removed plus a video overlay defeats all of it and still gets "green". That is acceptable only if the wording and the amount of engineering match it; today neither does.
- **There is a concrete hole on the server**: the official board ranks on the client-claimed final timer and accepts every verdict that is not red, and a fully fabricated offline log is amber (item 1 below).
- **Your race-mode direction is right, but the reason is slightly off.** Removing practice tools does not stop a determined cheater (the source is open). It does stop accidents, lock bugs and bad optics, and it removes about 25 patch sets from a run session. That last part is the real "near-vanilla" win.
- **A race build is blocked by three couplings**: runs start through the savestate system, the timer of record lives in `PracticeRunModule`, and the memory-leak fixes are owned by `SavestateModule`. All three must move before any flag can work.
- **Privacy defaults are wrong for a tool that asks for trust**: run uploads and attempt uploads are on by default with no consent step, the runner id is a reversible hash of the Steam id, runners cannot delete their own data, and Discord PB posts are decided by the site admin, not the runner.
- **The savestate "Quick load" is an unbounded fidelity chase** (about 14,000 lines, grade C in `docs/quality.md`, every current task is another edge case). It should be frozen and kept out of anything official.
- **The pure layer and its tests already exist** (`src/Data`, 1,345 tests, runs on Linux). What is untested is the 65,000 lines in `Modules/` and `Game/`, where the state machines actually live.

## Top 10 changes (ranked by impact over effort)

| # | What | Why | Files | Risk | Effort | In-game check |
|---|---|---|---|---|---|---|
| 1 | **Close the official-board hole.** Accept only green by default (amber per category, opt-in). Reject a `finished` log whose final timer is larger than its real span or far below it without matching `load` lines. Never list an attempt with no site nonce. | `Attempts.Official()` orders by `final_timer_ms` (the log's own `end` line) and accepts `VerdictOf(id) != "red"`. `Judge` makes an offline log amber and never compares the claimed timer with anything the server saw. Anyone with a token can post a self-consistent invented log and take first place once a category is published. | `site/ForestSite/Attempts.cs` (`Official`, `Judge`, `LogOnce`), `site/ForestSite.Tests/AttemptTests.cs` | Low. No category is published yet. | S | No |
| 2 | **Flip the privacy defaults.** `Site.UploadRuns` and `Site.SendAttempts` off until a one-time prompt is answered. Rename the keys so saved values do not carry over (the repo already uses that pattern). | Installing the plugin and timing one segment uploads the path, the times and the Steam name to a public board with no question asked. `CLAUDE.md` rule 13 exists only because of this default. | `src/Modules/RunUploadModule.cs:68`, `RunUploadModule.Attempts.cs:81`, `src/Core/NetworkSwitches.cs`, `SettingsModule.cs` | Low | S | Prompt only |
| 3 | **Move the leak fixes and the performance patches into an always-on `GameFixesModule`.** | `LeakedThreads`, `StaleSubscribers` and `PathPoolCleanup` are created and configured in `SavestateModule.Initialise` (lines 166-191). `PerfPatches` is owned by `DebugViewModule`. Any profile that drops practice or debug modules would silently drop the fixes runners care about most. | `src/Modules/SavestateModule.cs`, `DebugViewModule.cs`, new `GameFixesModule.cs`; `src/Game/` unchanged | Low (pure move, same config keys) | S | Yes, one load and a look at the log |
| 4 | **Stop rewriting the whole attempt log every 30 s on the main thread.** Append new lines to an open stream. Write the report to its own file. | `RunModeModule.SaveOpen` calls `FullLog()` (the whole `StringBuilder` to a string plus the report) and then `File.WriteAllText` twice (`WriteLocal`, `WriteOpenAttempt`). Late in a long run that is megabytes of garbage and two synchronous writes every 30 s, on a heap where each collection is a 90 ms frame. It happens only in run mode, where a hitch matters most. | `src/Modules/RunModeModule.Codes.cs` (`SaveOpen`, `FullLog`, `WriteLocal`), `RunUploadModule.Attempts.cs:97`, `src/Data/AttemptChain.cs` (`Text`) | Low. Keep crash recovery: the reader must accept a log with no `end`. | S | Yes, to measure |
| 5 | **Make the race boundary a registration decision.** Give `OverlayModule` a kind (Core, Practice, Dev). In the race profile, practice and dev modules are never constructed, so their `Initialise` never patches the game. | Today the lock is per entry point: 14 `Run.Refuse` and about 50 `Run.Locks` sites, with `Practice.Mark` as the net. A forgotten call is an open door. `SavestateModule.Initialise` alone installs about 20 patch sets at startup for every session. | `src/Core/OverlayModule.cs`, `ModuleHost.cs`, `src/Plugin.cs` (`BuildModules`), every module's kind | Medium. Needs items 3, 6 and 7 first or run mode breaks. | M | Yes |
| 6 | **Start runs through the game's own load, not the savestate restore.** A run spot's start state becomes a real save blob loaded by the game's loader with no fix-up pass. | `docs/run-mode.md` says a run starts with a savestate Full load. So an official run begins in a world rebuilt by the least reliable subsystem (families rebuilt, pickups removed, bushes cut again), and `RunModeModule` and `RunUploadModule` both `Find<SavestateModule>()`. | `src/Modules/RunModeModule.cs`, `SavestateModule.cs` (`RestoreStartState`), `src/Game/SavestateBridge.cs`, `TitleLoad.cs` | Medium to high. Whether a plain load is enough is a game fact. | M-L | Yes |
| 7 | **Extract the timer and splits from `PracticeRunModule` into `RunTimerModule`.** | One class, 14 partial files, 7,596 lines, about 330 fields. Run mode's timer is a `Func<long>` that this "practice" module sets on `RunModeModule`. It cannot be left out of a race build while the timer is inside it. | `src/Modules/PracticeRunModule*.cs`, `RunModeModule.Codes.cs`, new `RunTimerModule.cs` | Medium. Many fields are shared across the partials. | L | Yes |
| 8 | **Take the dev tooling out of the shipped DLL.** A `Dev` configuration (or a second project) for the bridge, probes, dumpers, profilers and QA module. Address modules by `Id` in the bridge. | About 10,000 lines that only the author and the agents use: `BridgeModule`, `ObjectProbe`, `InputInject`, `GameDumper`, `TypeExplorer`, `WorldDump`, `TerrainDump`, `AerialCapture`, `MapRelief`, `FsmExport`, `MemoryCensus`, `AllocationTracker`, `GameProfiler`, `RenderProbe`, `AnimProbe`, `SurfaceProbe`, `QaModule`, `DeveloperModule`. The bridge is a file-driven `call static:Type Method` reflection RPC inside the verification binary. `src/CLAUDE.md` freezes module order because the bridge addresses `_modules[9]`. | `ForestOverlay.csproj`, the files above, `tools/BridgeMcp`, `scripts/e2e.py`, `docs/bridge.md` | Medium. The e2e suite and the agents depend on the bridge, so they must run the dev build. | M | Yes (e2e on the dev build) |
| 9 | **Gate the always-on self-measurement in `ModuleHost`.** | `Tick` does, per module per frame: `GC.CollectionCount`, two `Stopwatch.GetTimestamp`, two `GC.GetTotalMemory`, two `StallWatch.At`. `DrawScreens`, `DrawScreensAlways` and `DrawPanels` add two heap reads per drawing module per OnGUI pass. With 26 modules this is a few hundred calls a frame to measure the overlay itself. | `src/Core/ModuleHost.cs`, `PerfMonitor.cs`, `StallWatch.cs` | Low | S | Optional (measure) |
| 10 | **Fix runner identity.** Use a random per-install id (the code already has `RandomRunnerId`), or a server-salted hash. Let a runner delete their own runs and attempts with their token. | `RunnerIdentity.HashId` is `SHA256("forestoverlay-runner|steam:" + id)` cut to 64 bits. Steam account ids are a 32-bit space, so the public `r-...` id maps back to the Steam profile in minutes. Because the id is derivable and `Store.Register` gives it to the first caller, anyone can register a victim's id before they do. Deleting is owner-only today. | `src/Game/RunnerIdentity.cs`, `site/ForestSite/Store.cs` (`Register`), `Program.cs`, `Runs.cs`, `Attempts.cs` | Medium. Existing ids need a migration. | M | No |

## Findings by area

### 1. Architecture

- **`Plugin.cs` is not "lifecycle and composition only"** (as `src/CLAUDE.md` says). Lines 344-611 draw the HUD column, measure it, and handle its drag. That belongs in a HUD class.
- **`Core` depends on `Modules`.** `OverlayModule.OpenMyTab` and `TabShowing` call `Host.Find<Modules.MainWindowModule>()`. `TabShowing` is a linear scan on each call.
- **"Modules never reach for each other" is not true.** There are 79 `Find<T>()` calls. The hubs are `SavestateModule` (found by 9 modules), `PracticeModule` (8) and `PracticeRunModule` (6). `RunModeModule` finds `BridgeModule`, `DeathModule`, `SavestateModule`, `RunUploadModule`. `SettingsModule` and `DeveloperModule` draw other modules' UI through `Find`. The contract is a service locator with a rule against using it.
- **God classes**: `PracticeRunModule` (7,596 lines over 14 files), `PracticeModule` (3,310), `SavestateModule` (2,786), `SavestateBridge` (2,416), `InputOverlayModule` (1,314), `DebugDraw` (1,200), `PickupKeeper` (1,186), `PerfPatches` (1,159). `docs/quality.md` already grades any file over 2,000 lines as C and nothing has been done about it.
- **Wrong owners**: leak fixes in `SavestateModule`, performance patches in `DebugViewModule`, item caps and fast build in `InventoryModule`, the run timer in `PracticeRunModule`, run categories fetched in `RunModeModule` but drawn through `CommunityModule` paths. Ownership follows the order features were added, not what they are.
- **`src/Data` is a folder, not a boundary.** 13 files use BepInEx (logging) and 27 use `UnityEngine` types. Tests compile it through 132 hand-written `<Compile Include>` lines and a Unity shim. The site links 18 of the same files plus the tests' shim. `Game/` has 9 files that reference `Core`.
- **Over-engineering**: the HUD editor (`HudWidgets*`, `HudSnap`, `HudProfile`, `HudLayout`, `HudTrend`, about 3,000 lines for placing text), a custom IMGUI kit (`UiKit`, `WinPx`, `CrispText`, `UiText`, `FontPick`), three overlapping watchdogs (`PerfMonitor`, `StallWatch`, `UnityErrorLog`), and two input-display systems (`InputOverlayModule` and the TAS input tracks).
- **Dead code**: `TimerModule.cs` is unregistered and still declares F8/F9 hotkeys. `src/GameDumper.cs` and `src/TypeExplorer.cs` sit outside every layer.
- **193 empty `catch (Exception) { }` blocks.** Most are deliberate ("never throw into the game"), but they also hide a game rename as silence. Each reflection lookup that fails should log once.
- **`ModuleHost.Disable` is one policy for all modules**: the first exception disables the module for the session with only a log line. For `RunModeModule` that means an open attempt stops stepping and the code on screen freezes with no notice. The timer and run mode need "show it on screen and keep going"; `Plugin.OnGUI`'s handler has the same problem (it hides the HUD for the session).

### 2. Bloat

See the cut table below.

### 3. Verification and anti-cheat

**What it proves.** A client holding this runner's token produced this log at about this wall-clock time, and its chain was not rewritten after each checkpoint. Nothing more. The positions, the timer, the flags and the whole integrity report are the client's own statements. `[report]` is not even folded into the chain.

**Why a fork beats it.** Remove the `Refuse` calls and the flag calls, build, and the report says a clean install. The fork can feed recorded positions into the chain, use savestates off camera, and paint the resulting codes over any footage, because the code box is a known rectangle and the codes are a function of a log the cheater holds. The server sees a matching chain in real time and answers green. The site's replay path and event timeline are the only things a moderator can compare with the video, and those are also client-written.

**Do the practice tools undermine it?** Not against an adversary: the tools are in the public source either way. They do undermine it in three other ways. First, a missed lock is silent (see item 5). Second, a run starts through a practice subsystem (item 6). Third, a moderator cannot tell runners "this binary cannot teleport". A race build fixes the second and third and makes the first impossible.

**Client-side detection is a treadmill.** T-0414 added a clock-ratio watch, movement-constant checks, MonoMod detour-map inspection, per-type IL hashing and a raw read of Mono's image struct. Each one catches a drop-in cheat DLL written by someone who will not also edit ForestOverlay. Each one is also a new way to break on an odd machine. `RunIntegrity.LoadedImageHash` reads `MonoImage*` at fixed offsets 16 and 24 from a private field, on every user's machine at startup. I would freeze this list, keep the cheap parts (plugin list, foreign Harmony patch owners, the game's `Cheats` statics, the DLL hash from the file), and stop there.

**Timing slack in `Attempts.Judge`.** `NonceMs` is the client's claim and is fine up to 30 s. A checkpoint may be 45 s late plus 1 percent drift. A log therefore has roughly 75 s plus 36 s per hour of timeline the server cannot place, and it is still green. This matters less than item 1 but it should be written down next to the verdict.

**Code entropy.** The on-screen code is 20 bits of the chain head, four Crockford characters. Brute force is not the relevant threat: a code is not a secret, it is an index into a public log. Two real issues:
- At one code a second, an hour has 3,600 codes in a space of about 1,000,000, so about six pairs repeat within one run. `FindCode` can return more than one place. Six characters (30 bits) would make that about 0.006.
- The things that must be unguessable already are: the site nonce is 128 bits from `RandomNumberGenerator`, tokens are 192 bits, the attempt id is 64 bits. The attempt id doubles as the only protection of an unlisted report, so if reports become private it should grow to 128 bits or sit behind the token.

**Privacy.**
- Uploads are on by default, with no consent step (item 2).
- An attempt report is public to anyone with the link and holds the runner's Steam name, the reversible id, a position trace each second, and the file names of every other mod and patcher on their machine. Resets are uploaded too.
- "Private until shared" is easy to add: the site already only lists accepted official runs. Add a `shared` flag set by the runner's token, and answer 404 without it.
- The Discord posts are server-side (`PbWebhook`, `PbSettings`). The fallback `FOREST_DISCORD_WEBHOOK` posts community-spot PBs whenever it is set, and runner-spot posts are an admin switch. The runner has no say. Make posting a per-runner opt-in stored with the runner row, default off.
- Runners cannot delete their own runs or attempts.

**Unflagged gameplay changes in a run.** `GameControlsModule` puts extra bindings into the game's Rewired maps (the example in its own header is the mouse wheel on jump). It is never refused and never recorded, by a decision dated 2026-10-19. That contradicts rule 1 of `docs/run-mode.md` ("the tool can do nothing the unmodded game cannot"). It should at least be written into the report. The default-on performance patches are the same kind of question: patch 10 removes a 1 s asset sweep from every load and patch 9 moves the endgame load to the background, in a category timed in real time with loads.

### 4. Game integration

- **Patch surface at startup, every session**: `GameEvents` (6 patches), `DeathHooks` (5), `ItemCounter`, `ItemCapPatch`, `FastBuild`, `BuildWatch`, `MoveWatch` (8), `AuditWatch` (5, including a prefix and postfix on `EventRegistry.Publish`), `LoadTiming`, `FrameTimer`, `PerfPatches` (16), `GarbagePatches` (7), `CameraTrim`, and from `SavestateModule.Initialise`: `PickupKeeper` (8), `PanelKeeper`, `BossHold`, `SetupHold`, `NatureKeeper`, `FireKeeper`, `GreebleKeeper`, `BreakableKeeper`, `CutsceneAudio`, `FullCapacityWatch`, `TitleLoad`, `NatureGuideKeeper`, `SlotInfoGuard`, `PathfindingWatch` (9), `WreckClearing`, `ElevatorRides`, `LeakedThreads`, `PathPoolCleanup`. A runner who never opens the Practice tab still runs all of them.
- **24 prefixes can skip the game's method, and there are 8 transpilers.** The ones I read (`WreckClearing.StartPrefix`, `PathfindingWatch.BlockerStartPrefix`, `SetupHold.StartPrefix`, `BossHold.EnterPrefix`) are gated on a restore window and fall through on any exception. They are well guarded, but "armed for N seconds after a restore" is state that a run should never be able to reach. Not installing them is safer than gating them.
- **`AuditWatch` on `EventRegistry.Publish`** is the hottest hook I found: every game event passes through our prefix and postfix. **[in-game]** to measure.
- **`DeathHooks.CheckDeathPrefix`** reads health with `FieldInfo.GetValue`, which boxes a float on each call. If `CheckDeath` runs every frame this is steady garbage; the project's own rule says to use `FastField`. **[in-game]**
- **`FullCapacityWatch.Prefix`** builds a `StackTrace` on each call. Fine if the method is rare. **[in-game]**
- **`ExternalModWatch.Tick`** runs every 3 s forever, in and out of run mode, and enumerates Harmony's patched methods and the AppDomain's assemblies each time.
- **The memory leak.** The three fixes are small, well reasoned and independent of everything else (about 500 lines). They are the most valuable code in the repository for a runner and they are the hardest to find, configure or keep (item 3). `PerfPatches` 5, 16 and the `GarbagePatches` are in the same class. Patches 7 to 14 change when the game does work, and belong behind the moderators' ruling.
- **OnGUI.** `Plugin.Update` turns `useGUILayout` off unless a window is open, which is the right call. I did not audit each module's `DrawScreen` for allocation; the repo has a tracker for that. **[unverified]**
- **Fragility.** Everything in `Game/` is string-named reflection, by rule 1. The game is no longer updated, so the risk is low in practice. The cost is 38,000 lines that the compiler cannot check.

### 5. Testability

- The pure layer exists and is tested on Linux: 129 of 134 `src/Data` files are linked into `tests/ForestOverlay.Tests` (1,345 test methods), and `site/ForestSite.Tests` covers the judge. CI runs both. The Windows VM is already not needed for this layer.
- What cannot be tested today is logic trapped in modules: the attempt lifecycle and chain cadence (`RunModeModule.Codes`: step each second, checkpoint each minute, flag folding, end reasons), the timer and split state machine (`PracticeRunModule` and `.Splits`), the upload outbox and retry rules (`RunUploadModule*`), `RunMode` and `PracticeState` (pure except for `Time.unscaledTime` and `Notice`), `HotkeyMap` clash rules, and the config migrations scattered through `Initialise` methods.
- Make `Data` a real project (`netstandard2.0`, same sources). Tests and the site take a `ProjectReference`; the plugin keeps compiling the sources in so the install stays one DLL. Replace `ManualLogSource` in the 13 files with an `Action<string>`. Give time to pure classes as a parameter.
- Keep the VM for: one launch, one load (leak fixes in the log), one run-mode attempt, one update. The e2e suite in `tests/e2e` already does this over the bridge.

### 6. Correctness risks and bugs

1. Official board accepts amber and ranks on a client-claimed timer (top 10, item 1).
2. Runner id is reversible and can be registered by someone else first (item 10).
3. Whole attempt log rewritten every 30 s on the main thread (item 4).
4. A single exception disables `RunModeModule` for the session with no on-screen notice (section 1).
5. `Judge` leaves about 75 s plus 1 percent of unplaced time in a green log (section 3).
6. 20-bit codes repeat about six times an hour; `FindCode` is ambiguous (section 3).
7. A flipped config default does not reach existing installs: BepInEx keeps the saved value. Any privacy change needs a new key.
8. `UpdateChecker` installs whatever `ForestOverlay.dll` the latest release holds, with no hash or signature. The patcher only checks the assembly name. For a tool that vouches for runs, the release should carry a SHA-256 in its notes and the plugin should check it.
9. `ReleaseJson.ExtractAssetUrl` and `SiteProtocol.Field` parse JSON by `IndexOf`. They work until a field order or an escape changes.
10. `CLAUDE.md` "Where we are" describes 2026-10-21 and v0.24.397; the tree is at v0.24.401. The router is already stale.
11. `docs/run-mode.md` says Reload save on death is "on for everyone today"; the code default is `GameDeath`. One of them is wrong.

### 7. Build, CI, release

- `build.yml` gives `contents: write` to every run, including pull requests. Split the release into its own job with that permission and leave the build job read-only.
- Actions are pinned by tag (`checkout@v5`, `softprops/action-gh-release@v2`), not by commit.
- The released DLL is compiled against BepInEx's UnityEngine 5.6.1 stub, not the game's 5.6.5. It works today; it is also the only build runners ever get and no test loads it. **[in-game]** smoke on each release already exists by hand.
- CI runs `scripts/lint.py` without `--tag`. The check that the tag equals the csproj version and `PluginVersion` runs only in the local pre-push hook. Add `--tag "$GITHUB_REF_NAME"` on tag builds.
- The csproj embeds `patcher/bin/$(Configuration)/net35/ForestOverlay.Updater.dll` by path. It depends on the `ProjectReference` building first with the same configuration name; an output-path change breaks it silently at pack time, loudly at run time.
- `site.yml` deploys the live site on any push to `main` that touches `src/Data/**` (rule 14). A plugin refactor in that folder is a production deploy. Narrow the paths to the files the site links, or deploy the site from tags.
- `site.yml` installs Playwright unpinned in the job that later holds the deploy key (already item 12 in `docs/security-audit-2026-10.md`, still open).
- 353 tags, sometimes twelve in one unattended session, each offered to every runner. There is no stable channel. A verifier cannot say which plugin versions are acceptable. Add a `stable` channel that moves by hand, and let the site name the versions a category accepts.
- The session-start hook reports CI as not passing for build, site and bot. I could not reach the Actions API from this session to confirm. **[unverified]**

### Rules in `CLAUDE.md` I think are mistakes

- **Rule 12** (never gate a feature on legality). It is the direct cause of practice, TAS and verification sharing one binary. Your race-mode direction replaces it.
- **Rule 13** (test runs upload to the live site). A symptom of uploads on by default and no staging URL. Fix the default and the rule can go.
- **Rule 14** (pushes deploy). See the `src/Data/**` point above.
- **`src/CLAUDE.md`: "a new module is registered last"**. A dev tool's addressing scheme dictates production order.
- **Rules 9 and 10 and the harness.** 15,800 lines of Python, 321 task files and a loop that is scored on closed tasks. It produced 400 releases and the long tail in the cut table. The harness is good at finishing tasks and has nothing that asks whether a task should exist. Add a scope gate before `tasks.py start` ("is this in v1.0 scope", answered by you), or the cuts below will grow back.

## Feature cut table

| Feature / subsystem | Main files | Rating | Reason |
|---|---|---|---|
| Module host, hotkeys, cursor and input hold | `Core/ModuleHost`, `HotkeyMap`, `CursorController`, `Game/GameInput` | Keep | The frame of everything; trim the self-measurement. |
| HUD column (item counts, velocity, timer line) | `Core/HudBuilder`, `Data/HudLines`, `Modules/RunInfoModule`, `InventoryModule` (read side) | Keep | The product's core. |
| HUD editor: free placement, snapping, profiles, trends | `Core/HudWidgets*`, `Data/HudSnap`, `HudProfile`, `HudLayout`, `HudTrend` | Simplify | About 3,000 lines to move text; one column with a position and a size is enough for race. |
| Custom UI kit and fonts | `Core/UiKit`, `WinPx`, `CrispText`, `UiText`, `FontPick` | Simplify | Keep `UiText`; drop the font picker and skinning options. |
| Leak fixes | `Game/LeakedThreads`, `StaleSubscribers`, `PathPoolCleanup` | Keep | Small, proven, what runners install the mod for. Move to `GameFixesModule`. |
| Garbage patches 24-27, sun material leak (16), asset-sweep merge (5) | `Game/GarbagePatches`, `PerfPatches` | Keep | Behaviour-preserving, measured. |
| Load and camera patches 7-14 | `Game/PerfPatches`, `CameraTrim`, `EndgameLoader` | Simplify | They change load timing in a real-time category; off in race until moderators rule; delete the experimental ones. |
| Run timer and splits | `PracticeRunModule` (core), `.Splits`, `Data/SplitTable`, `SplitSequence`, `RunTiming` | Keep | Extract to `RunTimerModule`. |
| LiveSplit `.lss` comparisons | `PracticeRunModule.LiveSplit`, `Data/LssFile`, `LssAutoSplit` | Merge | Two timers (in-game and the LiveSplit ASL in another repo); pick which one is of record, keep the other as a view. |
| Ghosts, race lines, speed graph, results panel | `PracticeRunModule.Lines`, `.LineMarks`, `.ExtraLines`, `.Graph`, `.Results`, `.Checkpoints` | Move-to-practice-only | Practice analysis, not needed in a run. |
| Replay, replay camera, replay bar, recordings | `PracticeRunModule.Replay`, `.ReplayBar`, `.Camera`, `.Recordings`, `Game/ReplayDraw`, `ArmsReplica`, `Data/ReplayCamera` | Move-to-practice-only | Large (about 3,500 lines); candidates to cut if practice needs trimming. |
| Spots, teleports, spot editor | `Modules/PracticeModule` (3,310) | Move-to-practice-only | Also split the file. |
| Savestates, Full load | `SavestateModule`, `Game/SavestateBridge`, `Data/SavestateFile` | Move-to-practice-only | Useful; remove it from the run start path. |
| Savestates, Quick load and its keepers | about 20 files: `PickupKeeper`, `NatureKeeper`, `FireKeeper`, `EnemyKeeper`, `AnimalKeeper`, `PanelKeeper`, `MeganKeeper`, `GreebleKeeper`, `BreakableKeeper`, `ElevatorKeeper`, `AreaKeeper`, `WeatherKeeper`, `BlueprintKeeper` and others | Simplify | Freeze the fidelity list. Label it "approximate". No new keeper without your sign-off. |
| Death handling: reload on death, revive, god mode, no stagger | `Modules/DeathModule`, `Game/DeathHooks`, `DeathSequence` | Move-to-practice-only | Reload-on-death in runs is a moderators' question, see below. |
| Item caps, logs in inventory, fast build | `InventoryModule` (write side), `Game/ItemCapPatch`, `LogStore`, `FastBuild` | Move-to-practice-only | They write game state; they sit in an "info-only" module. |
| Run mode: attempts, chain, codes, report | `Modules/RunModeModule*`, `Data/AttemptChain`, `RunReport`, `Core/RunMode` | Keep | Honest wording; fix items 1 and 4. |
| Integrity heuristics from T-0414 | `Game/RunIntegrity` (type hashes, image read, detours), `Data/SpeedCheck`, `Game/ClipWatch`, `SpeedRead` | Simplify | Keep the cheap list; freeze the rest. |
| Audit log and move detection | `RunModeModule.Audit`, `.Moves`, `Game/AuditWatch`, `MoveWatch`, `Data/RunAudit`, `MoveDetector` | Simplify | Useful to moderators as a timeline; it is evidence from the client, so keep it small. |
| Run categories from the site | `RunModeModule.Categories`, `Data/RunCategory`, `site/Categories.cs` | Keep | In race, a category should only be able to lock, never unlock god mode or item caps. |
| Run and attempt uploads | `Modules/RunUploadModule*`, `Data/SiteProtocol`, `AttemptStore` | Keep | Opt-in. |
| Community spots and segments | `Modules/CommunityModule*`, `Data/CommunityIndex`, `SiteSpots` | Move-to-practice-only | Practice content. |
| Collectibles (100 percent) checklist | `Modules/CollectiblesModule`, `Data/CollectionList` | Keep | Info-only, small; optional tab. |
| Map tab | `Modules/MapModule`, `Data/MapView`, `ReliefImage`, `Game/MapRelief` | Move-to-practice-only | Its main action is a teleport. |
| Input overlay | `Modules/InputOverlayModule` (1,314), `Data/InputLayout` | Simplify | Runners already use external input viewers; fixed layout if kept. |
| Extra game bindings | `Modules/GameControlsModule`, `Game/ExtraBindingMaps` | Move-to-practice-only | Changes what the game accepts as input; unflagged in runs today. |
| TAS record and replay | `Modules/TasModule`, `Game/TasInput`, `InputInject`, `Data/TasRecording` | Move-to-practice-only | Input injection must not exist in a race binary. |
| Trajectory preview, freecam | `Modules/DebugViewModule`, `Game/TrajectoryView`, `Data/Trajectory` | Move-to-practice-only | Already locked in runs. |
| Paint | `Modules/PaintModule`, `Game/PaintDraw`, `Data/PaintSet` | Move-to-practice-only | About 1,300 lines, self-contained, writes no game state. I rated it Cut for size; the author wants it kept (2026-11-17), and it costs nothing in the practice pack. |
| Free timer | `Modules/TimerModule` | Cut | Unregistered, dead. |
| Test bridge and probes | `Modules/BridgeModule`, `Game/ObjectProbe`, `AnimProbe`, `RenderProbe`, `SurfaceProbe`, `Data/BridgeCommand` | Cut from release (dev build) | Reflection RPC in the verification binary. |
| Dumpers and explorer | `GameDumper`, `TypeExplorer`, `Modules/DumpModule`, `ExplorerModule`, `Game/WorldDump`, `TerrainDump`, `AerialCapture`, `FsmExport`, `PrefabList` | Cut from release (dev build) | They feed the site map and the bot, not the runner. |
| Profilers and census | `Game/MemoryCensus`, `AllocationTracker`, `GameProfiler`, `LoadTiming`, `FrameTimer`, `Core/PerfMonitor`, `StallWatch` | Cut from release (dev build) | Their job is done: they found the leak. |
| QA module and Developer tab | `Modules/QaModule`, `DeveloperModule`, `Data/QaList`, `qa/` | Cut from release (dev build) | Team tooling. |
| Updater and patcher | `Core/UpdateChecker`, `UpdaterInstaller`, `patcher/` | Keep | Add a hash check and a stable channel. |
| ModAPI repair in the patcher | `patcher/ModApiFix.cs`, `DefaultRepair.cs`, `Game/ModApi` | Simplify | Rewriting another mod's game assembly with Cecil is a large liability; detect and say so instead. |
| Log keeper and error log | `Core/LogKeeper`, `UnityErrorLog`, `Data/LogArchive` | Keep | Cheap, and what makes bug reports usable. |
| Site: boards, attempts, admin | `site/ForestSite` | Keep | Fix items 1 and 10. |
| Site: 3D world and maps | `wwwroot/map3d.js`, `world3d.js`, the aerial and world uploads | Simplify | Grade C, 2,300 lines of JS plus the plugin's capture tools; park it. |
| Discord PB posts | `site/PbWebhook.cs`, `PbSettings.cs` | Simplify | Per-runner opt-in, off by default. |
| Knowledge bot and cards | `bot/`, `knowledge/`, `docs/fsm/` | Cut from this repo | Already paused; it is a separate product and it deploys from this repo's pushes. |
| Agent harness | `scripts/` (49 files), `tasks/`, `.claude/` | Simplify | Keep `tasks.py`, lint, bump and e2e; drop the loop until scope is fixed. |

## Target architecture

One repository, one codebase, three build outputs. The race boundary is an assembly boundary, so it is enforced by the compiler and visible to a moderator as "which files are installed".

```
ForestOverlay.Data        netstandard2.0 project (same sources also compiled into the plugin)
  formats, chain, report, categories, timer and split state machines,
  attempt lifecycle, outbox rules. No Unity, no BepInEx. All unit tests.

ForestOverlay.dll         net35, the race build, the only file a runner needs
  Core/      host, hotkeys, HUD column, notice, window shell, updater
  Game/      PlayerRef, InventoryReader, PlayerStateReader, GameEvents,
             GameLoading, RunIntegrity (slim), the leak and garbage fixes
  Modules/   Hud, RunTimer, RunMode, RunUpload, GameFixes, Settings, Update
  Exposes:   IRunTimer, IRunStart, IPracticeGate in ModuleContext

ForestOverlay.Practice.dll   net35, optional second plugin, depends on ForestOverlay.dll
  spots, teleports, savestates and keepers, ghosts, replay, TAS,
  death tools, item caps, map, community spots, trajectory, freecam

ForestOverlay.Dev.dll     net35, never released
  bridge, probes, dumpers, profilers, census, QA, Developer tab
```

Rules of the layout:

- Dependencies point one way: Dev to Practice to Core to Data. Core never names a practice type. `Find<T>()` is replaced by interfaces registered in `ModuleContext`.
- Race mode is the absence of `ForestOverlay.Practice.dll`. With it present, run mode still locks it at run time as today, and the report says "practice pack loaded, locked" or "not installed". A category can require "not installed". This is still a client statement, and it should be labelled as one.
- If you want a single file for the practice audience, merge the two DLLs at build time for a second release asset. A `#if` flag across 99,000 lines would be harder to keep correct than a project boundary.
- A run starts from the game's own load. Nothing in Core restores state.
- Uploads, attempt sending and Discord posts are off until the runner turns them on.

## Refactor plan

Each step is small, leaves the mod buildable, and can ship on its own. Steps marked **[in-game]** need the VM smoke test (launch, load a save, one run-mode attempt).

1. **Baseline.** Record lines per folder, the count of Harmony-patched methods the plugin logs at startup, and the e2e result on the current release. Later steps compare against it. **[in-game]**
2. **Site: official board.** Top 10 item 1, with tests in `AttemptTests.cs`. Site only.
3. **Privacy defaults.** Top 10 item 2: new config keys, a one-time prompt in the main window, the Runs tab says clearly whether sending is on. **[in-game]** for the prompt.
4. **`GameFixesModule`.** Move ownership of the three leak fixes out of `SavestateModule` and of `PerfPatches`, `GarbagePatches` and `CameraTrim` out of `DebugViewModule`. Same config sections and keys. Register it last (the current rule) so nothing shifts. **[in-game]**
5. **Append-only attempt log.** Top 10 item 4. Add a test that a truncated log still reads.
6. **Gate host instrumentation** behind `Diagnostics.MeasureModules` (default off). Top 10 item 9.
7. **Bridge addresses modules by `Id`.** Update `BridgeModule`, `tools/BridgeMcp`, `scripts/e2e.py` and `docs/bridge.md`, then delete the "registered last" rule from `src/CLAUDE.md`. **[in-game]** (e2e)
8. **Module kinds.** Add `Kind` to `OverlayModule` and set it on all 26 modules. No behaviour change. Add a lint: a Core module may not `Find<>` a Practice or Dev module. The lint will fail; list the failures as the work for steps 9 to 12.
9. **`IRunTimer`.** Define it in Core. `PracticeRunModule` implements it; `RunModeModule` takes it from `ModuleContext` instead of the `TimerMs` and `TimedLoadMs` funcs and `Find<>`. Mechanical.
10. **Extract `RunTimerModule`.** Move the timer state, `.Splits` and the splits panel out of `PracticeRunModule`. Move the state machine itself into `Data` with tests as it goes. Do it in two or three commits, one partial file at a time. **[in-game]**
11. **Native run start.** Prove over the bridge that a start state captured by a normal save loads correctly through the game's own path with no fix-ups, then switch `RunModeModule` to it behind `IRunStart`. If it cannot be proved, stop and ask (open question 3). **[in-game]**
12. **Cut the remaining Core to Practice lookups** (`RunModeModule` to `DeathModule` and `BridgeModule`, `RunUploadModule` to `SavestateModule`, `SettingsModule` drawing Paint and Game controls). The step 8 lint goes green.
13. **Race profile switch.** `[General] Profile = Race | Practice`. In Race, `BuildModules` skips Practice and Dev kinds. Check the patched-method count against step 1. **[in-game]**
14. **Dev configuration.** Move the dev files behind a `Dev` build configuration; the release workflow builds Release only. Agents and e2e use the Dev build. **[in-game]** (e2e)
15. **Delete**: `TimerModule`, the experimental performance patches, the font picker. One commit each. (`PaintModule` stays, in the practice pack.)
16. **Split the assemblies.** Create `ForestOverlay.Practice.csproj`; move the Practice modules and their `Game/` keepers. The updater learns to carry a second file. The report states whether the pack is loaded. **[in-game]**
17. **`ForestOverlay.Data` project.** Replace the 132 link lines in the tests and the 18 in the site with a project reference. Remove BepInEx from the 13 `Data` files. Narrow `site.yml` paths in the same commit so the move does not surprise-deploy.
18. **Identity and deletion.** Top 10 item 10, with a migration on the site.
19. **Verdict wording and codes.** Rename what green means on the attempt page ("the log matches what the site received, on time"), six-character codes, the `shared` flag for reports, per-runner Discord opt-in.
20. **CI and release.** Read-only build job, separate release job, actions pinned by commit, `lint.py --tag` on tags, SHA-256 in the release notes checked by `UpdateChecker`, a stable channel.
21. **HUD editor and input overlay trim**, last, because it is the least risky and the most visible.

Steps 2 to 7 are independent of each other and of the rest. Steps 9 to 13 are the race-mode spine and must go in order.

## Open questions for you

1. **What is verification for?** If it is "make honest runs easy to check and casual cheating awkward", the current chain is enough and the detection work should stop. If it is "prove a run is legitimate", no client-side design will do it and the effort is better spent on moderator process (hand-cam, live runs for records).
2. **Timer of record.** In a race, is it LiveSplit with the ASL, or the in-game timer? Keeping both as equals doubles the work on splits.
3. **Run start.** Are the preset category saves real game saves that the game can load by itself? If yes, step 11 is small. If a run must start mid-state in a way only the savestate system can produce, race mode keeps a dependency on it and you need to decide whether that is acceptable.
4. **One file or two.** The earlier decision was "the plugin DLL stays the whole install". Is a second, optional practice DLL acceptable, or do you want two merged release assets (race and full)?
5. **Performance patches in runs.** Leak and garbage fixes only, or also the load-time patches? This needs the moderators, because the category is timed with loads.
6. **Extra bindings (wheel jump) and reload-on-death in runs.** Allowed, allowed and recorded in the report, or practice-only?
7. **Amber.** Should an offline or late attempt ever appear on an official board? Today it does.
8. **Existing data.** With a new runner id scheme, do current runners keep their runs (migration by token) or start clean? Nothing is published yet, so starting clean is cheap now and not later.
9. **Quick load.** Freeze it as "approximate, practice only", or keep funding fidelity work? Six of the open savestate tasks are edge cases of it.
10. **The bot and the 3D map.** Move them to their own repositories, or delete? Both deploy from pushes to this one.
11. **The unattended loop.** Keep it off until the cut is done? Otherwise it will keep adding to the areas this review asks to shrink.
12. **Rule 1 (no `Assembly-CSharp` reference).** I would keep it for now. It is worth revisiting after the cut: a locally generated reference-only stub would let the compiler check 38,000 lines of string-named reflection, at the cost of a build step.

## Addendum: the author's answers (2026-11-17) and what they change

The open questions above were answered. This section records the answers and overrides the earlier sections where they differ.

### Decisions

| # | Answer | Effect on the plan |
|---|---|---|
| 1 | Verification exists so a runner can show that using the tool is not cheating, because an external tool counts as modding under the current rules. | See *What verification can honestly claim* below. The separate-files split becomes the main argument, not an option. |
| 2 | The tool replaces LiveSplit and the ASL entirely (the MCSR model). | The in-game timer is the timer of record. Step 10 gains weight: the timer and split state machine must live in `Data` with tests before anything else is built on it. `.lss` support shrinks to a one-time import of old splits. |
| 3 | Runs start from centralised preset saves. | Step 11 stands: load them through the game's own loader. Still needs the in-game proof. |
| 4 | Separate files are acceptable if that is the right end state. | It is. Step 16 is confirmed. |
| 5 | Load patches are wanted as QoL. Patches must not change game behaviour. | One conflict to settle first, see *One question left*. |
| 6 | Extra bindings are allowed as long as one key does not send several inputs. Reload on death is allowed if the loaded game is 1:1 with a menu load. | Two checks added below. |
| 7 | My choice. | Official boards list green only. Amber stays visible to the runner and can be approved by a moderator by hand. |
| 8 | Keep current data if cheap. | Keep it: one token-authenticated rekey endpoint moves a runner's rows to a new random id. If it grows past a day's work, start clean (nothing is published). |
| 9 | Quick load is fine as practice-only. | Frozen as "approximate, practice only". |
| 10 | Bot and 3D map go to their own repositories. | New early step: move `bot/`, `knowledge/`, `docs/fsm/` and the map capture tools out. This also removes two deploy triggers from this repo. |
| 11 | The unattended loop stays off until the cut is done. | No `work-loop` runs. Each refactor step is one attended session. |
| 12 | No change to rule 1 now. | None. |

### What verification can honestly claim

"Prove a run is legit" is not reachable from software on the runner's PC, with this tool or any other. The reachable claim is narrower and is the one to take to the moderators: **a run made with the tool is no easier to fake than an unmodded run, and it comes with more evidence.** Three things carry that claim, in order of weight:

1. **The run binary cannot cheat.** `ForestOverlay.dll` contains no teleport, savestate, input injection or reflection bridge. Its SHA-256 is published with each stable release and the build is reproducible from the tag, so anyone can check that the file is the source. This is the direct answer to "it is a mod".
2. **The server checks what it can check by itself.** Timing against its own clock (already there), plus plausibility of the log: position change per second against the game's movement limits, splits in route order, the final timer against the real span. A fork can delete client checks; it cannot delete server checks.
3. **Records get a human.** Green means "the log is consistent and on time". For a top time a moderator compares the site's path and event timeline with the video. Say this on the attempt page instead of implying the tool decided.

With the practice pack installed, run mode locks it as today and the report says "practice pack loaded, locked". Moderators can require "not installed" for records. This keeps "practise and run without restarting" for everyday attempts.

As far as I know, MCSR rests on the same three legs: an allowed-mod list, files submitted with the run, and moderator review. It also has something this game lacks, a seed that lets a run be re-simulated. Do not promise that level.

### Checks added by answers 5 and 6

- **Extra bindings.** I found a note that lists the actions sharing an input (`ExtraBindingMaps`, about line 180, shown by `GameControlsModule.ClashNote`). I did not find a refusal. Your rule needs one: reject a binding whose input already drives another action, the game's own bindings included, and write the active extra bindings into the run report. **[in-game]**
- **Reload on death.** The default path is `LevelSerializer.Resume` from in game (`Deaths.QuickLoadSkipMenu`, on). The code comment argues from IL that it equals the menu load, and `docs/run-mode.md` itself says it stays allowed only once both paths are compared with a manual reload (player, inventory, time of day, enemies, world, caves). I found no record of that comparison in `docs/confirmed.md`. Until it exists, run mode should use the menu path. `ReloadInPlace` stays practice-only, as it is today. **[in-game]**
- **Capture and boss wake-up.** `QuickLoadOnCapture` and `QuickLoadInBossFight` (both on) replace the game's first-death capture and the boss-room wake-up with a reload. That is a change of what a death does, not only of menuing. It is fine if a runner could do the same by quitting to the menu at that moment; that should be stated to the moderators. **[in-game]**
- **Performance patches, per patch.** Each one that stays on in the run binary needs a line in a table: what it changes, why the game computes the same result, and the A/B that showed it. `PerfPatches.cs` already has most of this in comments for patches 3, 4, 5, 10, 16 and 24 to 27. Patches 7, 13 and 14 are marked experimental by the code itself and stay out.

### One question left

You wrote that loads are not part of run timers. The repository's own rules card says the opposite: `knowledge/cards/categories-and-rules.md` states runs are timed in real time and that load times and paused time count. Loads do happen inside a run: every reload on death, and the endgame load behind the vault door (patch 9). So under the current speedrun.com rule, shorter loads are shorter runs.

Which is it going forward?

- **(a) Real time stays the ranked time.** Then the load patches make tool runs slightly faster than unmodded ones. That is acceptable if everyone moves to the tool, but old board times stop being comparable, and the moderators should agree to it knowingly.
- **(b) Load-removed time becomes the ranked time.** The plugin already measures it (`load` lines in the attempt log). Then load patches are pure comfort and the fairness question disappears. This is a rules change for the moderators.

I recommend (b). It fits "replace LiveSplit" and removes the hardware advantage as well. It is your call and theirs, not mine.

### Game-UI integration

Skip the deep version. Rebuilding the overlay inside the game's own UI system means more string-named reflection into the least documented part of the game, for looks only. The cheap version gives most of the feeling:

- One "ForestOverlay" entry in the game's pause menu that opens the window. Run mode already only opens the window over the pause menu, so this would become the single way in. **[in-game]**, and it needs a look at how the menu is built first.
- Status lines through the game's own bottom-left messages, which `Game/GameMessageLines` already does.
- Fewer HUD options (step 21), so the default look is the look.

### Revised order

1. Steps 2 to 7 as written (site hole, privacy defaults, `GameFixesModule`, append-only log, instrumentation gate, bridge by `Id`).
2. New: move the bot, the knowledge base and the map tooling to their own repositories.
3. Steps 8 to 13 (module kinds, `IRunTimer`, `RunTimerModule` with the state machine in `Data`, native run start, profile switch).
4. Steps 14 to 16 (dev build, deletions, the practice DLL), with the stable channel and published hashes from step 20 pulled forward to land with step 16.
5. New: server-side plausibility checks and the green-only board.
6. The remaining steps (17 to 21) in order.

### Second round of answers (2026-11-17)

- **Loads: settled.** Real time stays the ranked time; load-removed time is shown as an extra figure on tool runs. The author's clarification holds against the code: main-menu loads happen before a run starts, and patch 9's own comment says the vault-door cutscene ends when it would anyway (the frozen frame counts as game time). The "one question left" above is closed. The per-patch table for the moderators is still needed.
- **Extra bindings.** Refuse a binding whose input already drives another action (the game's own bindings included). The author sees a report line as redundant; I would still write one line, because "allowed and visible" is easier for moderators to approve than "allowed and invisible". Author's call.
- **Reload on death.** File a task: compare the in-game `LevelSerializer.Resume` path and the menu path against a manual menu reload (player, inventory, time of day, enemies, world, caves). Until it passes, run mode uses the menu path. Not filed in `tasks/tasks.jsonl` from this session (read-only review); the next session files it with `scripts/tasks.py`.
- **A simpler rule to consider.** The author says no run continues through a death reload; it is only a reset. If so, run mode can end the attempt on a death reload instead of carrying on through it (`docs/run-mode.md` says it carries on since v0.24.210). Then official runs never depend on the comparison at all.
- **Capture and boss wake-up toggles** are practice comfort. They move to the practice pack with the rest of the death tools.
- **UI.** Add the pause-menu entry; do not redesign the window before approval. A theme is cheap later if it is only colours and one font (`Data/UiPalette`, `Core/UiKit` hold them); textures and new layouts are the expensive part. A mockup can be drawn outside the game first.
- **For approval**, beyond the split binary and published hashes: a short run of dual-timed attempts (LiveSplit with the ASL beside the tool) to show the two timers agree, and a one-page description for the moderators of what the run binary does and does not do.
