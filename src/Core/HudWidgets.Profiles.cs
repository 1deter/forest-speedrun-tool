using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // HUD profiles (T-0019, Momentum Mod style): the whole HUD layout is a
    // named profile, one file each in `config/ForestOverlay/hud/` (Data/
    // HudProfile) so it can be shared. A profile holds the value layout and
    // every config entry registered here (RegisterProfileEntry): the HUD's
    // switches, place, Compact, Text size (HudSettings), the 100% totals,
    // the splits + results panels (author, 2026-10-11).
    //
    // The config stays the live store - the HUD and the panels read their
    // entries as before. A change to a registered entry marks the active
    // profile dirty and the next Tick writes its file (one write a frame at
    // most); switching applies a file's values to the entries (one config
    // save) and its layout. Switched only from the picker in Edit HUD mode
    // (author). The first launch with no profile makes "Default" from the
    // current config + the old hud-layout.txt.
    // ------------------------------------------------------------------
    public sealed partial class HudWidgets
    {
        private const string OldLayoutFile = "hud-layout.txt";

        private readonly ConfigFile _config;
        private readonly HudSettings _settings;
        private readonly string _configDir, _dir;
        private readonly ConfigEntry<string> _activeCfg;
        private readonly List<ConfigEntryBase> _entries = new List<ConfigEntryBase>();
        private readonly List<Action<object>> _appliers = new List<Action<object>>();
        private readonly List<string> _names = new List<string>();
        private GUIContent[] _nameTexts = new GUIContent[0];
        private bool _loaded, _applying, _dirty;

        public string ActiveProfile { get { return _activeCfg.Value; } }

        /// A setting that belongs to the HUD profile. `apply` (may be null)
        /// sets it when a profile is switched in, for an owner that caches
        /// its value; otherwise the entry is set directly.
        public void RegisterProfileEntry(ConfigEntryBase entry, Action<object> apply)
        {
            if (entry == null || _entries.Contains(entry)) return;
            _entries.Add(entry);
            _appliers.Add(apply);
        }

        private static string KeyOf(ConfigEntryBase e)
        {
            return e.Definition.Section + "." + e.Definition.Key;
        }

        private string PathOf(string name)
        {
            return Path.Combine(_dir, name + HudProfileNames.Extension);
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (!_loaded || _applying || args == null) return;
            if (_entries.Contains(args.ChangedSetting)) _dirty = true;
        }

        /// From the host's Tick: writes the active profile after a settings change.
        public void FlushProfile()
        {
            if (!_dirty) return;
            _dirty = false;
            SaveProfile();
        }

        // --- start -------------------------------------------------------------------

        /// After every module has registered its entries: makes "Default" the
        /// first time, then puts the active profile in.
        public void LoadProfiles()
        {
            if (_dir == null) { _loaded = true; return; }
            ScanNames();
            if (_names.Count == 0) MigrateToDefault();
            string name = _activeCfg.Value;
            if (!HudProfileNames.Contains(_names, name))
            {
                string pick = HudProfileNames.Contains(_names, HudProfileNames.DefaultName) ? HudProfileNames.DefaultName
                            : _names.Count > 0 ? _names[0] : null;
                if (_log != null) _log.LogWarning("HUD profile: '" + name + "' not found in " + _dir + " - using " + (pick ?? "the default look") + ".");
                name = pick;
            }
            _loaded = true;
            if (name == null) return;
            ApplyFile(name);
            if (_log != null) _log.LogInfo("HUD profile: " + name + " (" + _names.Count + " in " + _dir + ").");
        }

        private void MigrateToDefault()
        {
            HudProfile p = Capture();
            string old = Path.Combine(_configDir, OldLayoutFile);
            try
            {
                if (File.Exists(old)) p.Layout = HudLayout.Parse(File.ReadAllText(old));
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD profile: the old layout file not read (" + ex.Message + ") - Default starts with every value in the column.");
            }
            _layout = p.Layout;
            Rebuild();
            if (!Write(HudProfileNames.DefaultName, p)) return;
            _activeCfg.Value = HudProfileNames.DefaultName;
            _names.Add(HudProfileNames.DefaultName);
            BuildNameTexts();
            try
            {
                if (File.Exists(old)) File.Move(old, old + ".old");
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD profile: " + OldLayoutFile + " left in place (" + ex.Message + "); it is no longer read.");
            }
            if (_log != null) _log.LogInfo("HUD profile: Default made from the current HUD settings and layout.");
        }

        private void ScanNames()
        {
            _names.Clear();
            try
            {
                if (Directory.Exists(_dir))
                {
                    string[] files = Directory.GetFiles(_dir, "*" + HudProfileNames.Extension);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string n = Path.GetFileNameWithoutExtension(files[i]);
                        if (!string.IsNullOrEmpty(n) && !HudProfileNames.Contains(_names, n)) _names.Add(n);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD profile: folder not read (" + _dir + "): " + ex.Message);
            }
            HudProfileNames.Sort(_names);
            BuildNameTexts();
        }

        private void BuildNameTexts()
        {
            _nameTexts = new GUIContent[_names.Count];
            for (int i = 0; i < _names.Count; i++) _nameTexts[i] = new GUIContent(_names[i]);
            _activeText.text = _activeCfg.Value;
        }

        // --- files ---------------------------------------------------------------------

        private HudProfile Capture()
        {
            HudProfile p = new HudProfile();
            for (int i = 0; i < _entries.Count; i++)
                p.Set(KeyOf(_entries[i]), _entries[i].GetSerializedValue());
            p.Layout = _layout;
            return p;
        }

        private bool Write(string name, HudProfile p)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.WriteAllText(PathOf(name), p.Format());
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD profile '" + name + "' not saved: " + ex.Message);
                return false;
            }
        }

        /// The active profile's file, as the HUD is now.
        private void SaveProfile()
        {
            if (!_loaded || _dir == null || string.IsNullOrEmpty(_activeCfg.Value)) return;
            Write(_activeCfg.Value, Capture());
        }

        /// Reads profile `name` and puts it in: its settings into the config
        /// (one save), its layout on screen. A missing setting takes its default.
        private void ApplyFile(string name)
        {
            HudProfile p;
            try
            {
                p = HudProfile.Parse(File.Exists(PathOf(name)) ? File.ReadAllText(PathOf(name)) : "");
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("HUD profile '" + name + "' not read (" + ex.Message + ") - the default look.");
                p = new HudProfile();
            }

            _applying = true;
            bool saveEach = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            bool changed = false;
            try
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    ConfigEntryBase e = _entries[i];
                    object want = e.DefaultValue;
                    string stored = p.Get(KeyOf(e));
                    if (stored != null)
                    {
                        try { want = TomlTypeConverter.ConvertToValue(stored, e.SettingType); }
                        catch (Exception) { want = e.DefaultValue; }
                    }
                    if (Equals(want, e.BoxedValue)) continue;
                    if (_appliers[i] != null) _appliers[i](want);
                    else e.BoxedValue = want;
                    changed = true;
                }
            }
            finally
            {
                _config.SaveOnConfigSet = saveEach;
                _applying = false;
            }
            if (changed) _config.Save();

            _layout = p.Layout;
            Rebuild();
            if (_activeCfg.Value != name) _activeCfg.Value = name;
            _activeText.text = name;
            if (_settings != null) _settings.Touch();
        }

        // --- the picker's actions -------------------------------------------------------

        private string _profileStatus = "";
        private readonly GUIContent _profileStatusText = new GUIContent("");

        private void ProfileStatus(string s)
        {
            _profileStatus = s ?? "";
            _profileStatusText.text = _profileStatus;
        }

        private void SwitchTo(string name)
        {
            if (string.Equals(name, _activeCfg.Value, StringComparison.Ordinal)) return;
            CloseText();
            _renaming = false;
            FlushProfile();
            ApplyFile(name);
            ProfileStatus("");
            if (_log != null) _log.LogInfo("HUD profile: switched to " + name + ".");
        }

        private void NewProfile()
        {
            CloseText();
            FlushProfile();
            ScanNames();   // a file dropped in since Edit HUD opened is not overwritten
            string name = HudProfileNames.Unique("New profile", _names);
            if (!Write(name, new HudProfile())) { ProfileStatus("Could not write the file - see the log."); return; }
            _names.Add(name);
            HudProfileNames.Sort(_names);
            BuildNameTexts();
            ApplyFile(name);
            OpenRename();
            if (_log != null) _log.LogInfo("HUD profile: new profile " + name + " (the default look).");
        }

        private void DuplicateProfile()
        {
            CloseText();
            FlushProfile();
            string from = _activeCfg.Value;
            ScanNames();   // a file dropped in since Edit HUD opened is not overwritten
            string name = HudProfileNames.Unique(from + " copy", _names);
            if (!Write(name, Capture())) { ProfileStatus("Could not write the file - see the log."); return; }
            _names.Add(name);
            HudProfileNames.Sort(_names);
            BuildNameTexts();
            ApplyFile(name);
            OpenRename();
            if (_log != null) _log.LogInfo("HUD profile: " + from + " duplicated as " + name + ".");
        }

        private float _deleteArmedUntil = -1f;

        private void DeleteProfile()
        {
            if (_names.Count <= 1) return;
            if (Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedUntil = Time.unscaledTime + 3f;
                return;
            }
            _deleteArmedUntil = -1f;
            CloseText();
            _renaming = false;
            string name = _activeCfg.Value;
            try { File.Delete(PathOf(name)); }
            catch (Exception ex)
            {
                ProfileStatus("Could not delete the file: " + ex.Message);
                return;
            }
            _dirty = false;
            for (int i = 0; i < _names.Count; i++)
                if (string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase)) { _names.RemoveAt(i); break; }
            BuildNameTexts();
            ApplyFile(_names[0]);
            ProfileStatus("Deleted " + name + ".");
            if (_log != null) _log.LogInfo("HUD profile: " + name + " deleted - now " + _names[0] + ".");
        }

        private bool _renaming;
        private string _renameText = "";
        private string _renameProblem;
        private readonly GUIContent _renameProblemText = new GUIContent("");
        private bool _focusRename;

        private void OpenRename()
        {
            _renaming = true;
            _renameText = _activeCfg.Value;
            _renameProblem = null;
            _focusRename = true;
        }

        private void ConfirmRename()
        {
            string from = _activeCfg.Value;
            string to = (_renameText ?? "").Trim();
            if (string.Equals(to, from, StringComparison.Ordinal)) { _renaming = false; return; }
            List<string> others = new List<string>();
            for (int i = 0; i < _names.Count; i++)
                if (!string.Equals(_names[i], from, StringComparison.OrdinalIgnoreCase)) others.Add(_names[i]);
            _renameProblem = HudProfileNames.Problem(to, others);
            if (_renameProblem != null) { _renameProblemText.text = _renameProblem; return; }

            FlushProfile();
            try
            {
                // A change of case only: through a temporary name (Windows' Move
                // treats the two names as the same file).
                string src = PathOf(from), dst = PathOf(to);
                if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                {
                    string tmp = src + ".rename";
                    File.Move(src, tmp);
                    src = tmp;
                }
                File.Move(src, dst);
            }
            catch (Exception ex)
            {
                _renameProblem = "Could not rename the file: " + ex.Message;
                _renameProblemText.text = _renameProblem;
                return;
            }
            _renaming = false;
            _activeCfg.Value = to;
            ScanNames();
            ProfileStatus("");
            if (_log != null) _log.LogInfo("HUD profile: " + from + " renamed to " + to + ".");
        }

        // --- the picker (top of the Edit HUD list) --------------------------------------------------

        private static readonly GUIContent ProfileTitle = new GUIContent("Profile");
        private static readonly GUIContent ProfileTip = new GUIContent(
            "A profile is the whole HUD: which values show and where, their text, size, and the panels. " +
            "Each is a file in config/ForestOverlay/hud - copy one to share it.");
        private readonly GUIContent _activeText = new GUIContent("");
        private static readonly GUIContent NewText = new GUIContent("New");
        private static readonly GUIContent NewTip = new GUIContent("A new profile with the default HUD.");
        private static readonly GUIContent DuplicateText = new GUIContent("Duplicate");
        private static readonly GUIContent DuplicateTip = new GUIContent("A copy of this profile, to change without losing it.");
        private static readonly GUIContent RenameText = new GUIContent("Rename");
        private static readonly GUIContent DeleteText = new GUIContent("Delete");
        private static readonly GUIContent DeleteSureText = new GUIContent("Sure?");
        private static readonly GUIContent DeleteTip = new GUIContent("Deletes this profile's file. The last profile cannot be deleted.");
        private static readonly GUIContent OkText = new GUIContent("OK");
        private static readonly GUIContent CancelText = new GUIContent("Cancel");
        private const string RenameControl = "hudProfileName";
        private bool _scannedForEdit;

        /// The profile picker at (0, y) in the editor's scroll view; returns the y after it.
        private float DrawProfiles(float y, float cw)
        {
            if (!_scannedForEdit)
            {
                // A profile file dropped into the folder shows next time Edit HUD opens.
                _scannedForEdit = true;
                ScanNames();
            }
            if (!UiKit.Section(0f, ref y, cw, "hud.profiles", ProfileTitle, _activeText, ProfileTip, true)) return y;

            const float h = 22f;
            for (int i = 0; i < _names.Count; i++)
            {
                bool active = string.Equals(_names[i], _activeCfg.Value, StringComparison.Ordinal);
                if (UiKit.Toggle(new Rect(8f, y, cw - 8f, h), active, _nameTexts[i], GUI.skin.button) && !active)
                    SwitchTo(_names[i]);
                y += h + 2f;
            }
            y += 4f;

            if (_renaming)
            {
                Event e = Event.current;
                if (e != null && e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == RenameControl)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ConfirmRename(); e.Use(); }
                    else if (e.keyCode == KeyCode.Escape) { _renaming = false; e.Use(); }
                }
            }
            if (_renaming)
            {
                const float bw = 64f;
                GUI.SetNextControlName(RenameControl);
                _renameText = GUI.TextField(new Rect(8f, y, cw - 8f - (bw + 4f) * 2f, h), _renameText, HudProfileNames.MaxLength);
                if (_focusRename) { _focusRename = false; GUI.FocusControl(RenameControl); }
                if (UiKit.PrimaryButton(new Rect(cw - (bw + 4f) * 2f + 4f, y, bw, h), OkText)) ConfirmRename();
                if (GUI.Button(new Rect(cw - bw, y, bw, h), CancelText)) _renaming = false;
                y += h + 4f;
                if (_renameProblem != null) y += UiText.Draw(8f, y, cw - 8f, _renameProblemText, UiKit.HintStyle) + 4f;
            }
            else
            {
                float gap = 4f;
                float bw = (cw - 8f - gap * 3f) / 4f;
                float x = 8f;
                Rect r = new Rect(x, y, bw, h);
                if (GUI.Button(r, NewText)) NewProfile();
                UiKit.Hint(r, NewTip);
                r.x += bw + gap;
                if (GUI.Button(r, DuplicateText)) DuplicateProfile();
                UiKit.Hint(r, DuplicateTip);
                r.x += bw + gap;
                if (GUI.Button(r, RenameText)) OpenRename();
                r.x += bw + gap;
                bool canDelete = _names.Count > 1;
                bool armed = Time.unscaledTime <= _deleteArmedUntil;
                bool was = GUI.enabled;
                GUI.enabled = was && canDelete;
                if (GUI.Button(r, armed ? DeleteSureText : DeleteText)) DeleteProfile();
                GUI.enabled = was;
                UiKit.Hint(r, DeleteTip);
                y += h + 4f;
            }
            if (_profileStatus.Length > 0) y += UiText.Draw(8f, y, cw - 8f, _profileStatusText, UiKit.HintStyle) + 4f;
            return y + 4f;
        }
    }
}
