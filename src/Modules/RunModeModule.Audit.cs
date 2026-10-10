using System;
using System.Collections.Generic;
using System.Text;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The run audit log (docs/run-audit-and-replays.md part 1; author,
    // 2026-10-03: run mode attempts only, a skimmable rundown first, in
    // game first). During an attempt, what happened goes into the attempt
    // log as `event` lines (Data/AttemptChain, folded into the chain like
    // a move - never a flag): real ms, timer, kind, place, plain words.
    //
    // Sources, all already watched - nothing new is polled every frame:
    //   Game/GameEvents + Game/WorldEvents  endgame cutscenes, keycard doors,
    //       caves entered / left, clothing, passengers, the first input,
    //       ropes (the specific copies of an event - cave-enter-cave06,
    //       passenger-3, keycard-door-210 - are skipped: the general one
    //       carries the same thing as its detail)
    //   Game/ItemCounter  the bag, re-read only when an inventory method
    //       ran; changes merged by Data/RunAudit.ItemTally (a line after
    //       2 s quiet, at most every 10 s)
    //   Game/DeathHooks  each death the game decided and what was done
    //   Game/AuditWatch  the game's own event bus (built, crafted, used,
    //       kills, hits, trees, bombs, sleep, story, settings), the pause
    //       menu, the rides (read only for 3 s after an enter / exit)
    // A run writes at most RunAudit.MaxLines lines; one `audit-full` line
    // at the end counts the rest. Lines carry the time they are written
    // (the chain's times never go back), so a merged line says when it
    // began ("from 1:23").
    //
    // The Runs tab shows the last attempt's rundown and its last lines.
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule
    {
        private const int AuditShown = 10;          // the Runs tab's last lines
        private const float ItemReadEvery = 0.25f;  // the bag at most 4 times a second
        private const float RideReadEvery = 0.1f;
        private const float SettleAfterLoad = 2f;   // the bag fills over a load's first frames

        private ItemCounter _auditItems;
        private readonly Dictionary<string, int> _bag = new Dictionary<string, int>();
        private readonly RunAudit.ItemTally _tally = new RunAudit.ItemTally();
        private readonly RunAudit.Burst _trees = new RunAudit.Burst(RunAudit.Tree, "tree cut down", "trees cut down");
        private readonly RunAudit.Burst _hits = new RunAudit.Burst(RunAudit.Hit, "hit", "hits");

        private readonly List<AttemptChain.EventInfo> _attemptEvents = new List<AttemptChain.EventInfo>();
        private readonly List<string> _auditLast = new List<string>();
        private readonly GUIContent _auditText = new GUIContent("");
        private int _auditBuiltFor = -1;

        private int _eventsSeen, _deathsSeen, _auditLines, _auditSkipped;
        private bool _firstInputSeen, _paused, _auditLoaded;
        private long _pausedAtMs;
        private string _ride = "";
        private float _nextItemRead, _nextRideRead, _settleUntil;
        private bool _rideDirty;

        private void InitAudit(ModuleContext ctx)
        {
            _auditItems = new ItemCounter(ctx.Log, ctx.Inventory);
            InventoryReader names = ctx.Inventory;
            AuditWatch.ItemName = delegate(int id) { return names != null ? names.NameForId(id) : null; };
            AuditWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
        }

        /// A new attempt (its chain just began): nothing carries over.
        private void ResetAudit()
        {
            _eventsSeen = Ctx.Events != null ? Ctx.Events.Count : 0;
            _deathsSeen = DeathHooks.Deaths;
            _auditLines = 0;
            _auditSkipped = 0;
            _firstInputSeen = false;
            _paused = false;
            _ride = SafeRide();
            _rideDirty = false;
            _tally.Reset();
            _trees.Take(0, true);
            _hits.Take(0, true);
            _attemptEvents.Clear();
            _auditLast.Clear();
            _auditBuiltFor = -1;
            _settleUntil = Time.unscaledTime + SettleAfterLoad;
            AuditWatch.Pending.Clear();
        }

        private bool AuditOpen { get { return _attemptOpen && _chain != null && !_chain.Ended; } }

        // Every frame, from Tick.
        private void TickAudit(bool loaded)
        {
            bool open = AuditOpen;
            AuditWatch.Recording = open && loaded;
            if (!open)
            {
                if (Ctx.Events != null) _eventsSeen = Ctx.Events.Count;
                _deathsSeen = DeathHooks.Deaths;
                AuditWatch.Pending.Clear();
                RebuildAuditText();
                return;
            }
            long ms = _clock.ElapsedMilliseconds;

            // A load (Reload save on death): the bag is read again from a
            // fresh baseline once it has settled.
            if (loaded != _auditLoaded)
            {
                _auditLoaded = loaded;
                FlushItems(ms);
                _tally.Reset();
                _settleUntil = Time.unscaledTime + SettleAfterLoad;
                if (!loaded && _paused) _paused = false;   // a load closes the menu
            }

            try { TakeGameEvents(ms); } catch (Exception e) { AuditError("game events", e); }
            try { TakeDeaths(ms); } catch (Exception e) { AuditError("deaths", e); }
            try { TakeWatched(ms); } catch (Exception e) { AuditError("game event bus", e); }
            if (loaded)
            {
                try { TakeRide(ms); } catch (Exception e) { AuditError("rides", e); }
                try { TakeItems(ms); } catch (Exception e) { AuditError("items", e); }
            }
            TakeBurst(_trees, ms, false);
            TakeBurst(_hits, ms, false);
            RebuildAuditText();
        }

        private void AuditError(string what, Exception e)
        {
            Ctx.Log.LogWarning("Run audit: " + what + " skipped this frame: " + e.Message);
        }

        // --- sources -------------------------------------------------------------

        private void TakeGameEvents(long ms)
        {
            GameEvents ev = Ctx.Events;
            if (ev == null) return;
            int count = ev.Count;
            for (int i = _eventsSeen; i < count; i++)
            {
                string name = ev.NameAt(i);
                string detail = ev.DetailAt(i);
                if (name == GameEvents.AnyCutscene)
                {
                    // The named cutscene follows in the same frame; only an
                    // unknown one is written as itself.
                    if (i + 1 < count && IsEndgameEvent(ev.NameAt(i + 1))) continue;
                    Write(ms, name, "a cutscene no known routine started");
                    continue;
                }
                if (name == GameEvents.KeycardDoor)
                {
                    // vault-door / gold-door follow it with the door's name.
                    for (int j = i + 1; j < count && j <= i + 3; j++)
                    {
                        string next = ev.NameAt(j);
                        if (next == "vault-door" || next == "gold-door") { detail = GameEvents.LabelFor(next) + " - " + detail; break; }
                    }
                    Write(ms, name, detail);
                    continue;
                }
                if (name == WorldEvents.CaveEnter || name == WorldEvents.CaveExit)
                {
                    Write(ms, name, CaveText(detail));
                    continue;
                }
                if (name.StartsWith(WorldEvents.Clothing + "-", StringComparison.Ordinal))
                {
                    Write(ms, WorldEvents.Clothing, detail ?? name.Substring(WorldEvents.Clothing.Length + 1));
                    continue;
                }
                if (name == WorldEvents.Passenger) { Write(ms, name, "passenger " + detail); continue; }
                if (name == WorldEvents.FirstInput)
                {
                    if (_firstInputSeen) continue;
                    _firstInputSeen = true;
                    Write(ms, name, "the runner took control");
                    continue;
                }
                if (name == WorldEvents.RopeGrab || name == WorldEvents.RopeLeave) { Write(ms, name, null); continue; }
                if (IsEndgameEvent(name)) { Write(ms, name, detail); continue; }
                // Everything else is a copy of the above (cave-enter-cave06,
                // passenger-3, keycard-door-210, vault-door, game-end) or a
                // run start trigger (first-input, hold-interact).
            }
            _eventsSeen = count;
        }

        private static bool IsEndgameEvent(string name)
        {
            if (name == GameEvents.KeycardDoor || name == GameEvents.RedElevator) return true;
            for (int i = 0; i < GameEvents.Hooks.Length; i++)
                if (GameEvents.Hooks[i].Event == name) return true;
            return false;
        }

        private static string CaveText(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string label = WorldEvents.CaveLabel(id);
            return label != null ? label : id;
        }

        private void TakeDeaths(long ms)
        {
            int n = DeathHooks.Deaths;
            if (n == _deathsSeen) return;
            int missed = n - _deathsSeen - 1;
            _deathsSeen = n;
            Write(ms, RunAudit.Death, DeathText(DeathHooks.LastKind, DeathHooks.LastAction) +
                                      (missed > 0 ? " (and " + missed + " more the same frame)" : ""));
        }

        private static string DeathText(DeathKind kind, DeathAction action)
        {
            string what;
            switch (kind)
            {
                case DeathKind.Capture: what = "the first death (the game wakes you up captured in a cave)"; break;
                case DeathKind.BossWake: what = "died in the Megan fight (the game wakes you up)"; break;
                case DeathKind.PermaDeath: what = "died in Hard Survival (the game deletes the save)"; break;
                case DeathKind.Multiplayer: what = "died in multiplayer"; break;
                default: what = "died"; break;
            }
            switch (action)
            {
                case DeathAction.QuickLoad: return what + " - Reload save on death loads the save (through the title screen)";
                case DeathAction.QuickLoadInGame: return what + " - Reload save on death loads the save";
                case DeathAction.Revive: return what + " - revived at the practice spot";
                default: return what + " - the game's own death";
            }
        }

        /// The save loaded after Reload save on death: the attempt goes on.
        private void AuditReloaded()
        {
            if (!AuditOpen) return;
            Write(_clock.ElapsedMilliseconds, RunAudit.Reload, "the save is loaded - the attempt goes on");
        }

        private void TakeWatched(long ms)
        {
            List<AuditWatch.Raw> pending = AuditWatch.Pending;
            if (pending.Count == 0) return;
            for (int i = 0; i < pending.Count; i++)
            {
                AuditWatch.Raw r = pending[i];
                switch (r.Kind)
                {
                    case RunAudit.PauseOpen:
                        if (_paused) break;
                        _paused = true;
                        _pausedAtMs = ms;
                        Write(ms, r.Kind, null);
                        break;
                    case RunAudit.PauseClose:
                        if (!_paused) break;
                        _paused = false;
                        Write(ms, r.Kind, "open " + Seconds(ms - _pausedAtMs));
                        break;
                    case RunAudit.Tree: AddBurst(_trees, ms, r.Detail); break;
                    case RunAudit.Hit: AddBurst(_hits, ms, r.Detail); break;
                    default: Write(ms, r.Kind, r.Detail); break;
                }
            }
            pending.Clear();
        }

        private void TakeRide(long ms)
        {
            float now = Time.unscaledTime;
            bool dirty = now < AuditWatch.RideDirtyUntil;
            // One more read once the window closes, then nothing until the
            // next enter / exit.
            if (!dirty && !_rideDirty) return;
            _rideDirty = dirty;
            if (dirty && now < _nextRideRead) return;
            _nextRideRead = now + RideReadEvery;
            string ride = SafeRide();
            if (ride == _ride) return;
            if (_ride.Length > 0) Write(ms, RunAudit.RideEnd, _ride);
            if (ride.Length > 0) Write(ms, RunAudit.RideStart, ride);
            _ride = ride;
        }

        private static string SafeRide()
        {
            try { return RideModes.Current() ?? ""; }
            catch (Exception) { return ""; }
        }

        private void TakeItems(long ms)
        {
            float now = Time.unscaledTime;
            if (now < _settleUntil || now < _nextItemRead) { FlushItemsIfDue(ms); return; }
            _nextItemRead = now + ItemReadEvery;
            _bag.Clear();
            if (_auditItems.Fill(_bag, !_tally.HasBaseline)) _tally.Update(_bag, ms);
            FlushItemsIfDue(ms);
        }

        private void FlushItemsIfDue(long ms)
        {
            long since = _tally.PendingSince;
            string line = _tally.Take(ms, false);
            if (line.Length > 0) Write(ms, RunAudit.Items, line + From(since, ms));
        }

        private void FlushItems(long ms)
        {
            long since = _tally.PendingSince;
            string line = _tally.Take(ms, true);
            if (line.Length > 0) Write(ms, RunAudit.Items, line + From(since, ms));
        }

        private void AddBurst(RunAudit.Burst b, long ms, string what)
        {
            if (b.Closes(ms)) TakeBurst(b, ms, true);
            Vector3 p;
            bool has = PlayerPos(out p);
            b.Add(ms, TimerMs != null ? TimerMs() : -1, has, p.x, p.y, p.z, what);
        }

        private void TakeBurst(RunAudit.Burst b, long ms, bool force)
        {
            long first = b.FirstMs;
            bool has = b.HasPos;
            float x = b.X, y = b.Y, z = b.Z;
            long timer = b.TimerMs;
            string line = b.Take(ms, force);
            if (line.Length > 0) WriteAt(ms, timer, b.Kind, has, x, y, z, line + From(first, ms));
        }

        // " (from 1:23)" when a merged line began well before it is written.
        private static string From(long since, long ms)
        {
            return since >= 0 && ms - since >= 1500 ? " (from " + RunAudit.Clock(since) + ")" : "";
        }

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.0") + " s";
        }

        // --- writing ---------------------------------------------------------------

        private void Write(long ms, string kind, string detail)
        {
            Vector3 p;
            bool has = PlayerPos(out p);
            WriteAt(ms, TimerMs != null ? TimerMs() : -1, kind, has, p.x, p.y, p.z, detail);
        }

        // The player's place, false during a load (a destroyed transform).
        private bool PlayerPos(out Vector3 p)
        {
            p = Vector3.zero;
            if (!Ctx.Player.Found || PlayerRef.AtTitleScreen) return false;
            try { p = Ctx.Player.Transform.position; return true; }
            catch (Exception) { return false; }
        }

        private void WriteAt(long ms, long timer, string kind, bool hasPos, float x, float y, float z, string detail)
        {
            if (!AuditOpen) return;
            if (_auditLines >= RunAudit.MaxLines) { _auditSkipped++; return; }
            _auditLines++;
            if (ms < _chain.LastMs) ms = _chain.LastMs;   // the chain's times never go back
            _chain.Event(ms, timer, kind, hasPos, x, y, z, detail);

            AttemptChain.EventInfo e = new AttemptChain.EventInfo();
            e.RealMs = ms; e.TimerMs = timer; e.Kind = kind; e.HasPos = hasPos; e.X = x; e.Y = y; e.Z = z;
            e.Detail = AttemptChain.Clean(detail);
            _attemptEvents.Add(e);
            _auditLast.Add(RunAudit.Clock(ms) + "  " + RunAudit.Label(kind) + (e.Detail.Length > 0 ? ": " + e.Detail : ""));
            if (_auditLast.Count > AuditShown) _auditLast.RemoveAt(0);
        }

        /// The attempt is about to end: what is still being merged goes in.
        private void FlushAudit()
        {
            if (!AuditOpen) return;
            long ms = _clock.ElapsedMilliseconds;
            try
            {
                TakeGameEvents(ms);
                TakeDeaths(ms);
                TakeWatched(ms);
            }
            catch (Exception e) { AuditError("the last events", e); }
            FlushItems(ms);
            TakeBurst(_trees, ms, true);
            TakeBurst(_hits, ms, true);
            if (_auditSkipped > 0)
            {
                // Past the budget on purpose: the one line that says so.
                if (ms < _chain.LastMs) ms = _chain.LastMs;
                _chain.Event(ms, TimerMs != null ? TimerMs() : -1, RunAudit.Full, false, 0, 0, 0,
                             _auditSkipped + " later events not written (the log keeps " + RunAudit.MaxLines + ")");
                AttemptChain.EventInfo e = new AttemptChain.EventInfo();
                e.RealMs = ms; e.Kind = RunAudit.Full; e.Detail = _auditSkipped + " later events";
                _attemptEvents.Add(e);
                Ctx.Log.LogInfo("Run audit: the log was full - " + _auditSkipped + " event(s) not written.");
            }
            AuditWatch.Recording = false;
            Ctx.Log.LogInfo("Run audit: " + _attemptEvents.Count + " event line(s) in " + _attemptId + "'s log.");
        }

        // --- the Runs tab -------------------------------------------------------

        // Only when the events changed (never in OnGUI).
        private void RebuildAuditText()
        {
            int key = _attemptEvents.Count * 2 + (AuditOpen ? 1 : 0);
            if (key == _auditBuiltFor) return;
            _auditBuiltFor = key;
            if (_attemptEvents.Count == 0)
            {
                _auditText.text = AuditWatch.Status.StartsWith("watching") && !AuditWatch.Status.Contains("not found") ? ""
                                : "Run audit: " + AuditWatch.Status + ".";
                return;
            }
            StringBuilder sb = new StringBuilder(512);
            sb.Append("What happened in attempt ").Append(_report != null ? _report.Attempt.ToString() : "?")
              .Append(AuditOpen ? " so far" : "").Append(" (in its log - the attempt page shows the full timeline):");
            List<string> rundown = RunAudit.Rundown(_attemptEvents);
            for (int i = 0; i < rundown.Count; i++) sb.Append("\n- ").Append(rundown[i]);
            sb.Append("\nLast ").Append(_auditLast.Count == 1 ? "line" : _auditLast.Count + " lines").Append(':');
            for (int i = 0; i < _auditLast.Count; i++) sb.Append("\n  ").Append(_auditLast[i]);
            _auditText.text = sb.ToString();
        }

        private float DrawAudit(float y, float w)
        {
            if (_auditText.text.Length == 0) return y;
            return y + UiText.Draw(0, y, w, _auditText) + 4f;
        }
    }
}
