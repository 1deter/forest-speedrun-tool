using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A kept animal's health, hurt skin and poison across a Quick load
    // (T-0425; game-notes *An animal's health, hurt skin and poison*).
    // `animalHealth` is not in the save and a Quick load touches none of
    // it, and T-0415 keeps the animal object, so what happened to it since
    // the capture stayed. The capture writes each live animal's health, the
    // four _Damage floats of its skin's property block and whether it was
    // poisoned (Data/AnimalStates); a Quick load of the same world and
    // launch puts health and skin back and stops a poison started since.
    // Poison running at the capture is left as it is (the game keeps no
    // time left). Reflection only; no game name leaves this file.
    // ------------------------------------------------------------------
    public sealed class AnimalKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] DamageNames = { "_Damage1", "_Damage2", "_Damage3", "_Damage4" };

        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _health, _skin;
        private bool _resolved, _ok;
        private MaterialPropertyBlock _block;

        public AnimalKeeper(ManualLogSource log) { _log = log; }

        private bool Resolve()
        {
            if (_resolved) return _ok;
            _resolved = true;
            try
            {
                _type = GameBridge.FindGameType("animalHealth");
                if (_type != null)
                {
                    _health = _type.GetField("Health", Inst);
                    _skin = _type.GetField("mySkin", Inst);
                }
            }
            catch (Exception ex) { _log.LogWarning("AnimalKeeper: resolving failed: " + ex.Message); }
            _ok = _type != null && _health != null && _health.FieldType == typeof(int);
            if (!_ok) _log.LogWarning("AnimalKeeper: animalHealth.Health not found; animals are left as they are.");
            return _ok;
        }

        private Renderer SkinOf(Component ah)
        {
            return _skin != null ? _skin.GetValue(ah) as Renderer : null;
        }

        private float[] ReadSkin(Component ah)
        {
            float[] d = new float[4];
            Renderer r = SkinOf(ah);
            if (r == null) return d;
            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            for (int i = 0; i < 4; i++) d[i] = _block.GetFloat(DamageNames[i]);
            return d;
        }

        private static bool Poisoned(Component ah)
        {
            MonoBehaviour mb = ah as MonoBehaviour;
            return mb != null && mb.IsInvoking("HitPoison");
        }

        /// The `animals` line: every live (active) animal now.
        public string Capture()
        {
            if (!Resolve()) return "";
            try
            {
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                List<AnimalStates.Entry> list = new List<AnimalStates.Entry>();
                for (int i = 0; i < all.Length; i++)
                {
                    Component ah = all[i] as Component;
                    if (ah == null || ah.gameObject == null) continue;
                    AnimalStates.Entry e = new AnimalStates.Entry();
                    e.Id = ah.gameObject.GetInstanceID();
                    e.Health = (int)_health.GetValue(ah);
                    e.Damage = ReadSkin(ah);
                    e.Poisoned = Poisoned(ah);
                    list.Add(e);
                }
                return AnimalStates.Format(list);
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: capture failed: " + ex.Message);
                return "";
            }
        }

        /// After a Quick load; `line` is the file's `animals` line (null: an
        /// older file), `sameWorld` that the file is from this world and
        /// launch (instance ids mean something). Returns the log note.
        public string Restore(string line, bool sameWorld)
        {
            if (!sameWorld || string.IsNullOrEmpty(line) || !Resolve()) return "";
            try
            {
                List<AnimalStates.Entry> entries = AnimalStates.Parse(line);
                if (entries.Count == 0) return "";
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                Dictionary<int, Component> live = new Dictionary<int, Component>();
                for (int i = 0; i < all.Length; i++)
                {
                    Component ah = all[i] as Component;
                    if (ah != null && ah.gameObject != null) live[ah.gameObject.GetInstanceID()] = ah;
                }
                int animals = 0, health = 0, skin = 0, poison = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    AnimalStates.Entry e = entries[i];
                    Component ah;
                    if (!live.TryGetValue(e.Id, out ah) || ah == null) continue;
                    bool changed = false;
                    if ((int)_health.GetValue(ah) != e.Health)
                    {
                        _health.SetValue(ah, e.Health);
                        health++; changed = true;
                    }
                    float[] now = ReadSkin(ah);
                    bool differs = false;
                    for (int k = 0; k < 4; k++) if (now[k] != e.Damage[k]) differs = true;
                    Renderer r = SkinOf(ah);
                    if (differs && r != null)
                    {
                        r.GetPropertyBlock(_block);
                        for (int k = 0; k < 4; k++) _block.SetFloat(DamageNames[k], e.Damage[k]);
                        r.SetPropertyBlock(_block);
                        skin++; changed = true;
                    }
                    MonoBehaviour mb = ah as MonoBehaviour;
                    if (!e.Poisoned && mb != null && (mb.IsInvoking("HitPoison") || mb.IsInvoking("disablePoison")))
                    {
                        mb.CancelInvoke("HitPoison");
                        mb.CancelInvoke("disablePoison");
                        poison++; changed = true;
                    }
                    if (changed) animals++;
                }
                return AnimalStates.Describe(animals, health, skin, poison);
            }
            catch (Exception ex)
            {
                _log.LogWarning("AnimalKeeper: restore failed: " + ex.Message);
                return "animals: failed (" + ex.Message + ")";
            }
        }
    }
}
