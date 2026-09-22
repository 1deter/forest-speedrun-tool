using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    public enum DeathKind
    {
        /// First death in single player: the game knocks you out and you
        /// wake up captured in a cave. Quick-load treats it like any other
        /// death - no current route relies on being captured (author).
        Capture,
        /// A death that ends in GameOver -> TitleScene.
        Real,
        /// Dying in the Megan boss fight wakes you up (EndgameWakeUp).
        BossWake,
        /// Hard survival: the game deletes the save on death, so there is
        /// nothing to quick-load.
        PermaDeath,
        /// Multiplayer - never touched.
        Multiplayer,
    }

    public enum DeathAction { Normal, Revive, QuickLoad }

    // ------------------------------------------------------------------
    // Intercepts the player's death at the moment it is decided.
    //
    // WHERE DEATH STARTS (IL, PlayerStats):
    //   CheckDeath  - Health <= 0 and not already Dead: swimming ->
    //                 DeathInWater (drowning; always ends in GameOver),
    //                 else Dead = true -> FallDownDead -> ... KillPlayer.
    //                 Reached from Hit and Explosion; returns at once
    //                 under GodMode.
    //   Fell        - Health -= 200; if <= 0, Dead = true -> KillPlayer.
    //                 Sent by name (no IL callers), from fall triggers.
    //   KillPlayer  - DeadTimes++; SP: fighting the boss -> EndgameWakeUp;
    //                 DeadTimes > 1 -> dead cam, Invoke("GameOver", 6);
    //                 else the capture (wake up in a cave).
    //   GameOver    - SceneManager.LoadScene("TitleScene").
    //
    // A prefix on CheckDeath and Fell asks the module what to do, before
    // any of the death sequence has run:
    //   Normal     - let the game die as usual.
    //   Revive     - practice only: health and blood reset, death skipped;
    //                the module then teleports to the practice spot.
    //   QuickLoad  - call GameOver now (skipping the fall and the 6 s dead
    //                cam); the module then drives the title screen's own
    //                load of the same slot.
    //
    // The prefixes only skip the original when an action is taken, and
    // never throw into the game.
    // ------------------------------------------------------------------
    public sealed class DeathHooks
    {
        /// Set by the module. Called from inside the game's death check -
        /// keep it cheap and non-throwing.
        public static Func<DeathKind, DeathAction> Decide;

        /// Raised after a revive or a quick-load has been started, for the
        /// module to finish (teleport / drive the title screen).
        public static Action<DeathKind, DeathAction> Handled;

        private static ManualLogSource _log;

        private static FieldInfo _health;
        private static FieldInfo _healthTarget;
        private static FieldInfo _deadTimes;
        private static FieldInfo _permaDeath;
        private static FieldInfo _bloodAmount;
        private static FieldInfo _bloodRatio;
        private static FieldInfo _animControl;
        private static FieldInfo _swimming;
        private static PropertyInfo _isInEndgame;
        private static PropertyInfo _isFightingBoss;
        private static PropertyInfo _boltRunning;
        private static MethodInfo _gameOver;

        private Harmony _harmony;

        public string Status { get; private set; }

        public DeathHooks(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            Type stats = GameBridge.FindGameType("PlayerStats");
            if (stats == null) { Status = "PlayerStats not found"; _log.LogWarning("DeathHooks: " + Status); return; }

            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            _health = stats.GetField("Health", inst);
            _healthTarget = stats.GetField("HealthTarget", inst);
            _deadTimes = stats.GetField("DeadTimes", inst);
            _gameOver = stats.GetMethod("GameOver", inst, null, Type.EmptyTypes, null);
            _isFightingBoss = stats.GetProperty("IsFightingBoss", inst);

            Type cheats = GameBridge.FindGameType("Cheats");
            if (cheats != null) _permaDeath = cheats.GetField("PermaDeath", stat);

            Type bleed = GameBridge.FindGameType("BleedBehavior");
            if (bleed != null)
            {
                _bloodAmount = bleed.GetField("BloodAmount", stat);
                _bloodRatio = bleed.GetField("BloodReductionRatio", stat);
            }

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (local != null)
            {
                _animControl = local.GetField("AnimControl", stat);
                _isInEndgame = local.GetProperty("IsInEndgame", stat);
            }

            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            if (anim != null) _swimming = anim.GetField("swimming", inst);

            // BoltNetwork lives in another assembly (bolt); search them all.
            Type bolt = FindAnyType("BoltNetwork");
            if (bolt != null) _boltRunning = bolt.GetProperty("isRunning", stat);

            if (_health == null || _deadTimes == null)
            {
                Status = "PlayerStats.Health/DeadTimes not found";
                _log.LogWarning("DeathHooks: " + Status);
                return;
            }

            try
            {
                _harmony = new Harmony(harmonyId + ".deaths");
                int n = 0;
                n += Patch(stats, "CheckDeath", "CheckDeathPrefix");
                n += Patch(stats, "Fell", "FellPrefix");
                Status = n + "/2 death hooks" + (_gameOver == null ? ", no GameOver - quick-load off" : "");
            }
            catch (Exception ex)
            {
                Status = "Harmony unavailable: " + ex.Message;
            }

            _log.LogInfo("DeathHooks: " + Status + ".");
        }

        private int Patch(Type t, string method, string prefix)
        {
            MethodInfo target = t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public |
                                                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (target == null)
            {
                _log.LogWarning("DeathHooks: PlayerStats." + method + " not found.");
                return 0;
            }

            try
            {
                _harmony.Patch(target, new HarmonyMethod(typeof(DeathHooks).GetMethod(prefix,
                    BindingFlags.Static | BindingFlags.NonPublic)));
                return 1;
            }
            catch (Exception ex)
            {
                _log.LogWarning("DeathHooks: could not hook " + method + ": " + ex.Message);
                return 0;
            }
        }

        // ------------------------------------------------------------------
        // Prefixes. Return true to let the game run its own method.

        private static bool CheckDeathPrefix(object __instance)
        {
            try
            {
                float health = (float)_health.GetValue(__instance);
                if (health > 0f) return true;
                if (IsDead(__instance)) return true;
                if (GodMode()) return true;

                // Drowning is always a real death (DeathInWater ->
                // KillMeFast -> GameOver), whatever DeadTimes says.
                return Handle(__instance, IsSwimming() ? ClassifyRealOnly() : Classify(__instance));
            }
            catch (Exception) { return true; }
        }

        private static bool FellPrefix(object __instance)
        {
            try
            {
                float health = (float)_health.GetValue(__instance);
                if (health - 200f > 0f) return true;
                if (IsDead(__instance)) return true;

                return Handle(__instance, Classify(__instance));
            }
            catch (Exception) { return true; }
        }

        private static bool Handle(object stats, DeathKind kind)
        {
            if (Decide == null) return true;

            DeathAction action = Decide(kind);
            if (action == DeathAction.Revive)
            {
                Revive(stats);
            }
            else if (action == DeathAction.QuickLoad && _gameOver != null)
            {
                _log.LogInfo("Death (" + kind + "): quick-loading.");
                _gameOver.Invoke(stats, null);
            }
            else
            {
                return true;
            }

            if (Handled != null) Handled(kind, action);
            return false;
        }

        // Full health and a clean screen - the values PlayerStats.Awake
        // starts with. The teleport is the module's job.
        private static void Revive(object stats)
        {
            _health.SetValue(stats, 100f);
            if (_healthTarget != null) _healthTarget.SetValue(stats, 100f);
            if (_bloodAmount != null) _bloodAmount.SetValue(null, 0f);
            if (_bloodRatio != null) _bloodRatio.SetValue(null, 1f);
            _log.LogInfo("Death: revived (practice).");
        }

        /// Clears the blood overlay on demand - it builds up after
        /// repeated fall damage.
        public static void ClearBlood()
        {
            try
            {
                if (_bloodAmount != null) _bloodAmount.SetValue(null, 0f);
                if (_bloodRatio != null) _bloodRatio.SetValue(null, 1f);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // Mirrors KillPlayer's branches, evaluated before it runs: DeadTimes
        // is incremented inside KillPlayer, so "> 1 after" is ">= 1 now".
        private static DeathKind Classify(object stats)
        {
            if (Multiplayer()) return DeathKind.Multiplayer;
            if (InBossFight(stats)) return DeathKind.BossWake;

            int deadTimes = (int)_deadTimes.GetValue(stats);
            if (deadTimes < 1) return DeathKind.Capture;

            return PermaDeath() ? DeathKind.PermaDeath : DeathKind.Real;
        }

        private static DeathKind ClassifyRealOnly()
        {
            if (Multiplayer()) return DeathKind.Multiplayer;
            return PermaDeath() ? DeathKind.PermaDeath : DeathKind.Real;
        }

        private static bool IsDead(object stats)
        {
            FieldInfo dead = stats.GetType().GetField("Dead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return dead != null && (bool)dead.GetValue(stats);
        }

        private static bool GodMode()
        {
            Type cheats = GameBridge.FindGameType("Cheats");
            if (cheats == null) return false;
            FieldInfo f = cheats.GetField("GodMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null && (bool)f.GetValue(null);
        }

        private static bool PermaDeath()
        {
            return _permaDeath != null && (bool)_permaDeath.GetValue(null);
        }

        private static bool Multiplayer()
        {
            try { return _boltRunning != null && (bool)_boltRunning.GetValue(null, null); }
            catch (Exception) { return false; }
        }

        private static bool InBossFight(object stats)
        {
            try
            {
                bool endgame = _isInEndgame != null && (bool)_isInEndgame.GetValue(null, null);
                if (!endgame) return false;
                return _isFightingBoss != null && (bool)_isFightingBoss.GetValue(stats, null);
            }
            catch (Exception) { return false; }
        }

        private static bool IsSwimming()
        {
            try
            {
                if (_animControl == null || _swimming == null) return false;
                object anim = _animControl.GetValue(null);
                return anim != null && (bool)_swimming.GetValue(anim);
            }
            catch (Exception) { return false; }
        }

        private static Type FindAnyType(string name)
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    Type t = all[i].GetType(name, false);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            return null;
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }
    }
}
