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

    public enum DeathAction { Normal, Revive, QuickLoad, QuickLoadInGame }

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
    //   QuickLoadInGame - skip the death (health back to full so it cannot
    //                re-trigger before the scene goes), and the module loads
    //                the slot from in game: LevelSerializer.Resume(), the
    //                call LoadSave.Awake makes after the title screen. Skips
    //                the title scene and the first of a menu load's two
    //                game-scene loads. GameOverNow() is the fallback.
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

        // The PlayerStats of the last handled death, for GameOverNow().
        private static object _lastStats;

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
                PatchLanding();
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
                _revives++;
                Revive(stats);
                _log.LogInfo("Death: revived (practice).");
            }
            else if (action == DeathAction.QuickLoad && _gameOver != null)
            {
                _log.LogInfo("Death (" + kind + "): quick-loading.");
                _gameOver.Invoke(stats, null);
            }
            else if (action == DeathAction.QuickLoadInGame)
            {
                _log.LogInfo("Death (" + kind + "): quick-loading without the menu.");
                _lastStats = stats;
                Revive(stats);
            }
            else
            {
                return true;
            }

            if (Handled != null) Handled(kind, action);
            return false;
        }

        // ------------------------------------------------------------------
        // A revive from a fall still played the hard landing: stagger, 1 s
        // frozen, slow look (author). FirstPersonCharacter.HandleLanded
        // applies the fall damage with PlayerStats.Hit - where the revive
        // happens - and THEN, for a hard landing: Animator
        // "landHeavyTrigger", HitReactions.StartCoroutine("doHardfallRoutine")
        // (clampInputVal = 0 and velocity zeroed every frame for 1 s),
        // MainRotator.rotationSpeed = 0.55, CanJump = false, arm layers
        // (1-4) weighted to 0, and Invoke("resetAnimSpine", 1), whose
        // smoothEnableSpine fades layers 1 and 4 back in over 0.5 s and then
        // sets jumpCoolDown = false, CanJump = true,
        // HitReactions.disableControllerFreeze(), rotationSpeed = 5. When a
        // revive happened inside this call, the postfix stops the routine
        // and the trigger and applies that END state at once, cancelling the
        // delayed reset so it cannot fade the arms out again. v0.22.5 did
        // only the first part: no stagger, but no jump and arms down for
        // about a second (author).
        private static int _revives;
        private static FieldInfo _hitReactions;     // static LocalPlayer.HitReactions
        private static FieldInfo _animator;         // static LocalPlayer.Animator
        private static FieldInfo _mainRotator;      // static LocalPlayer.MainRotator
        private static FieldInfo _clampInput;       // FirstPersonCharacter.clampInputVal
        private static FieldInfo _rotationSpeed;    // SimpleMouseRotator.rotationSpeed
        private static FieldInfo _canJump;          // FirstPersonCharacter.CanJump
        private static FieldInfo _jumpLand;         // FirstPersonCharacter.jumpLand
        private static FieldInfo _jumpCoolDown;     // FirstPersonCharacter.jumpCoolDown
        private static MethodInfo _unfreeze;        // playerHitReactions.disableControllerFreeze()

        private void PatchLanding()
        {
            Type fpc = GameBridge.FindGameType("FirstPersonCharacter");
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type rot = GameBridge.FindGameType("SimpleMouseRotator");
            if (fpc == null || local == null) { _log.LogWarning("DeathHooks: no landing hook (types missing)."); return; }

            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _hitReactions = local.GetField("HitReactions", stat);
            _animator = local.GetField("Animator", stat);
            _mainRotator = local.GetField("MainRotator", stat);
            _clampInput = fpc.GetField("clampInputVal", inst);
            if (rot != null) _rotationSpeed = rot.GetField("rotationSpeed", inst);
            _canJump = fpc.GetField("CanJump", inst);
            _jumpLand = fpc.GetField("jumpLand", inst);
            _jumpCoolDown = fpc.GetField("jumpCoolDown", inst);
            Type reactionsType = GameBridge.FindGameType("playerHitReactions");
            if (reactionsType != null) _unfreeze = reactionsType.GetMethod("disableControllerFreeze", inst, null, Type.EmptyTypes, null);

            MethodInfo landed = fpc.GetMethod("HandleLanded", inst, null, Type.EmptyTypes, null);
            if (landed == null) { _log.LogWarning("DeathHooks: FirstPersonCharacter.HandleLanded not found."); return; }

            try
            {
                _harmony.Patch(landed,
                    new HarmonyMethod(typeof(DeathHooks).GetMethod("LandedPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(DeathHooks).GetMethod("LandedPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
                _log.LogInfo("DeathHooks: landing hook installed (hitReactions:" + (_hitReactions != null) +
                             " animator:" + (_animator != null) + " clamp:" + (_clampInput != null) +
                             " look:" + (_rotationSpeed != null) + " jump:" + (_canJump != null) +
                             " unfreeze:" + (_unfreeze != null) + ").");
            }
            catch (Exception ex)
            {
                _log.LogWarning("DeathHooks: could not hook HandleLanded: " + ex.Message);
            }
        }

        private static void LandedPrefix(out int __state)
        {
            __state = _revives;
        }

        private static void LandedPostfix(object __instance, int __state)
        {
            if (_revives == __state) return;   // no revive during this landing
            try
            {
                MonoBehaviour reactions = _hitReactions != null ? _hitReactions.GetValue(null) as MonoBehaviour : null;
                if (reactions != null) reactions.StopCoroutine("doHardfallRoutine");

                // The delayed reset would fade the arms out and in again.
                MonoBehaviour fpc = __instance as MonoBehaviour;
                if (fpc != null) fpc.CancelInvoke("resetAnimSpine");

                Animator a = _animator != null ? _animator.GetValue(null) as Animator : null;
                if (a != null)
                {
                    a.ResetTrigger("landHeavyTrigger");
                    // smoothEnableSpine's end: layer 4 unless drawing a bow, and 1.
                    if (!a.GetBool("drawBowBool")) a.SetLayerWeight(4, 1f);
                    a.SetLayerWeight(1, 1f);
                }

                if (_clampInput != null) _clampInput.SetValue(__instance, 1f);
                if (_jumpLand != null) _jumpLand.SetValue(__instance, false);
                if (_jumpCoolDown != null) _jumpCoolDown.SetValue(__instance, false);
                if (_canJump != null) _canJump.SetValue(__instance, true);
                if (reactions != null && _unfreeze != null) _unfreeze.Invoke(reactions, null);

                object rotator = _mainRotator != null ? _mainRotator.GetValue(null) : null;
                if (rotator != null && _rotationSpeed != null) _rotationSpeed.SetValue(rotator, 5f);   // the game's own value

                _log.LogInfo("Death: revived from a fall - hard landing cancelled.");
            }
            catch (Exception ex)
            {
                _log.LogWarning("Death: could not cancel the hard landing: " + ex.Message);
            }
        }

        // Full health and a clean screen - the values PlayerStats.Awake
        // starts with. The teleport is the module's job.
        private static void Revive(object stats)
        {
            _health.SetValue(stats, 100f);
            if (_healthTarget != null) _healthTarget.SetValue(stats, 100f);
            if (_bloodAmount != null) _bloodAmount.SetValue(null, 0f);
            if (_bloodRatio != null) _bloodRatio.SetValue(null, 1f);
        }

        /// The menu path, for when an in-game quick-load could not start:
        /// GameOver on the stats of the death that was skipped.
        public static bool GameOverNow()
        {
            if (_gameOver == null || _lastStats == null) return false;
            try
            {
                UnityEngine.Object o = _lastStats as UnityEngine.Object;
                if (o != null && o == null) return false;   // scene already gone
                _gameOver.Invoke(_lastStats, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// Called when a load finishes: the skipped death's stats belong to
        /// the destroyed world by then (a holder the memory census listed).
        public static void ForgetDeath()
        {
            _lastStats = null;
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
