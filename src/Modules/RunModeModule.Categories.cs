using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Run categories (run mode phase 4; Data/RunCategory).
    //
    // The moderators publish them on the site (/admin, seeded from
    // speedrun.com). Fetched a few seconds after startup, every 2 minutes,
    // when an attempt ends or a run spot starts one (author, 2026-10-02:
    // "Check categories" may never be pressed) and on the button - with
    // the last answer's ETag, so an unchanged list is a 304 and no body.
    // A new version reaches the next attempt, never the running one (the
    // attempt holds its category). Kept in
    // config/ForestOverlay/categories.txt for offline starts. A run spot's `run = ...` names its category (by id or name);
    // Start run mode uses the one picked in the Runs tab. The category goes
    // to Core/RunMode (what is locked, allowed, forced), the report (id +
    // version, the game) and the attempt's start on the site.
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule
    {
        private const string CategoriesFile = "categories.txt";
        private const float FetchDelay = 6f;
        private const float RefreshEvery = 120f;
        private float _refreshAt = -1f;
        private string _etag;
        private bool _fetchFailed;   // logged once until the next success

        private List<RunCategory> _categories = new List<RunCategory>();
        private ConfigEntry<string> _pickedCfg;
        private string _catStatus = "";
        private bool _fetching;
        private float _fetchAt = -1f;
        private readonly GUIContent _catText = new GUIContent("");
        private readonly GUIContent _catStatusText = new GUIContent("");
        // What the category text was last built for - compared field by
        // field, never as a key string: this runs every frame, and a string
        // key built per frame was most of the overlay's idle garbage, so the
        // game's ~85 ms garbage collection kept landing in this tick
        // ("Slow tick: 'runmode'", v0.24.241).
        private bool _catBuilt;
        private bool _catBuiltActive;
        private RunCategory _catBuiltShown;
        private int _catBuiltVersion;
        private int _catBuiltCount;
        private string _catBuiltStatus;

        private void InitCategories(ModuleContext ctx)
        {
            _pickedCfg = ctx.Config.Bind("RunMode", "Category", "",
                "The run category Start run mode uses (its id on the site). A run spot's own category wins.");
            try
            {
                string path = Path.Combine(ctx.ConfigDirectory, CategoriesFile);
                if (File.Exists(path))
                {
                    _categories = RunCategory.Parse(File.ReadAllText(path, Encoding.UTF8));
                    _catStatus = _categories.Count + " categories from the last check (offline copy).";
                }
            }
            catch (Exception ex) { _catStatus = "The saved categories could not be read: " + ex.Message; }
            _fetchAt = Time.unscaledTime + FetchDelay;
        }

        private void TickCategories()
        {
            if (_fetchAt >= 0f && Time.unscaledTime >= _fetchAt && !_fetching)
            {
                _fetchAt = -1f;
                FetchCategories();
            }
            else if (_refreshAt >= 0f && Time.unscaledTime >= _refreshAt && !_fetching)
            {
                _refreshAt = -1f;
                FetchCategories();
            }
        }

        /// A check now, without the button (an attempt ended, a run spot is
        /// starting one): a 304 when nothing changed.
        private void CheckCategoriesSoon()
        {
            if (!_fetching) _fetchAt = Time.unscaledTime;
        }

        private void FetchCategories()
        {
            string site = _upload != null ? _upload.SiteUrl : null;
            _refreshAt = Time.unscaledTime + RefreshEvery;
            if (string.IsNullOrEmpty(site)) { _catStatus = "No site address set (Settings) - categories not checked."; return; }
            _fetching = true;
            Ctx.Runner.StartCoroutine(Fetch(SiteProtocol.TrimUrl(site) + "/api/categories.txt"));
        }

        private IEnumerator Fetch(string url)
        {
            long code = 0;
            string body = null, error = null, etag = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", url, null, null, null, 20f,
                _etag != null ? new[] { "If-None-Match", _etag } : null, "ETag",
                delegate(long c, string b, string e, string h) { code = c; body = b; error = e; etag = h; }));
            _fetching = false;
            string when = DateTime.Now.ToString("HH:mm");
            if (code == 304)
            {
                _fetchFailed = false;
                _catStatus = _categories.Count + " categories from the site (checked " + when + ", no change).";
                yield break;
            }
            if (error != null || code != 200 || body == null)
            {
                _catStatus = "The site's categories could not be read at " + when + " (" + (error ?? "HTTP " + code) + ")" +
                             (_categories.Count > 0 ? " - using the " + _categories.Count + " from the last check." : ".");
                if (!_fetchFailed) Ctx.Log.LogWarning("Run mode: categories not fetched from " + url + ": " + (error ?? "HTTP " + code) + ".");
                _fetchFailed = true;
                yield break;
            }
            _fetchFailed = false;
            _etag = string.IsNullOrEmpty(etag) ? null : etag;
            List<RunCategory> list = RunCategory.Parse(body);
            string changed = Changes(_categories, list);
            _categories = list;
            _catStatus = (list.Count == 0 ? "The site has no published categories yet" : list.Count + " categories from the site") +
                         " (checked " + when + ")." + (changed.Length > 0 && Ctx.Run.Active ? " Changed: " + changed + " - from the next attempt." : "");
            Ctx.Log.LogInfo("Run mode: " + list.Count + " categories from the site" + (changed.Length > 0 ? " (changed: " + changed + ")" : "") + ".");
            if (changed.Length > 0 && Ctx.Run.Active)
                Ctx.Notice.Show("Run categories updated (" + changed + ") - they apply from the next attempt.", 6f);
            try { File.WriteAllText(Path.Combine(Ctx.ConfigDirectory, CategoriesFile), body, new UTF8Encoding(false)); }
            catch (Exception ex) { Ctx.Log.LogWarning("Run mode: categories not saved: " + ex.Message); }
        }

        /// "Any% - Normal v4, Manhunt (new)": what differs between two lists
        /// (empty: the same).
        private static string Changes(List<RunCategory> before, List<RunCategory> now)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < now.Count; i++)
            {
                RunCategory old = RunCategory.Find(before, now[i].Id);
                if (old == null) parts.Add(now[i].Name + " (new)");
                else if (old.Version != now[i].Version) parts.Add(now[i].Name + " v" + now[i].Version);
            }
            for (int i = 0; i < before.Count; i++)
                if (RunCategory.Find(now, before[i].Id) == null) parts.Add(before[i].Name + " (removed)");
            return string.Join(", ", parts.ToArray());
        }

        /// The category a run starts under: the run spot's, else the picked one.
        private RunCategory CategoryFor(Segment runSpot)
        {
            if (runSpot != null) return RunCategory.Find(_categories, runSpot.RunCategory);
            return RunCategory.Find(_categories, _pickedCfg.Value);
        }

        private RunCategory Picked { get { return RunCategory.Find(_categories, _pickedCfg.Value); } }

        private void Pick(int step)
        {
            if (_categories.Count == 0) return;
            int i = _categories.IndexOf(Picked);
            i = i < 0 ? (step > 0 ? 0 : _categories.Count - 1) : (i + step + _categories.Count) % _categories.Count;
            _pickedCfg.Value = _categories[i].Id;
            _catBuilt = false;
        }

        /// What a category allows, in a line or two.
        public static string Describe(RunCategory c)
        {
            if (c == null) return "";
            StringBuilder sb = new StringBuilder();
            List<string> game = new List<string>();
            if (c.Difficulty != "any") game.Add(char.ToUpperInvariant(c.Difficulty[0]) + c.Difficulty.Substring(1));
            if (c.Creative == "yes") game.Add("Creative");
            if (c.Creative == "no") game.Add("survival");
            if (c.Multiplayer == "yes") game.Add("multiplayer");
            if (c.Multiplayer == "no") game.Add("single player");
            sb.Append(game.Count > 0 ? "Played in: " + string.Join(", ", game.ToArray()) + ". " : "Any game setup. ");
            List<string> allowed = new List<string>(), forced = new List<string>();
            for (int i = 0; i < RunCategory.Features.Length; i++)
            {
                RunCategory.Feature f = RunCategory.Features[i];
                string p = c.Policy(f.Key);
                if (p == RunCategory.Allowed) allowed.Add(f.Label);
                else if (p == RunCategory.Forced) forced.Add(f.Label);
            }
            if (forced.Count > 0) sb.Append("On for everyone: ").Append(string.Join(", ", forced.ToArray())).Append(". ");
            if (c.IsForced("logs")) sb.Append("Logs held: ").Append(c.EffectiveLogCap).Append(". ");
            if (c.IsForced("itemcaps"))
            {
                if (c.ItemCaps.Count == 0) sb.Append("Item caps: none set (the game's). ");
                else
                {
                    sb.Append("Item caps: ");
                    for (int i = 0; i < c.ItemCaps.Count; i++)
                        sb.Append(i > 0 ? ", " : "").Append(c.ItemCaps[i].Key).Append(' ').Append(c.ItemCaps[i].Value);
                    sb.Append(". ");
                }
            }
            if (allowed.Count > 0) sb.Append("Your choice: ").Append(string.Join(", ", allowed.ToArray())).Append(". ");
            sb.Append("Everything else is locked.");
            if (!c.AntiSplice) sb.Append(" No anti-splice code on screen.");
            if (c.Banned.Count > 0) sb.Append(" Banned: ").Append(string.Join("; ", c.Banned.ToArray())).Append('.');
            return sb.ToString();
        }

        private void RebuildCategoryText()
        {
            RunCategory shown = Ctx.Run.Active ? Ctx.Run.Category : Picked;
            bool active = Ctx.Run.Active;
            int version = shown != null ? shown.Version : 0;
            if (_catBuilt && active == _catBuiltActive && ReferenceEquals(shown, _catBuiltShown) && version == _catBuiltVersion &&
                _categories.Count == _catBuiltCount && string.Equals(_catStatus, _catBuiltStatus)) return;
            _catBuilt = true;
            _catBuiltActive = active;
            _catBuiltShown = shown;
            _catBuiltVersion = version;
            _catBuiltCount = _categories.Count;
            _catBuiltStatus = _catStatus;
            if (Ctx.Run.Active)
                _catText.text = shown != null ? "Category: " + shown.Label + ". " + Describe(shown)
                                              : "Category: none the site knows - every practice feature is locked, Reload save on death is yours.";
            else
                _catText.text = shown != null ? "Category for Start run mode: " + shown.Label + ". " + Describe(shown) +
                                                " (A run spot uses its own category.)"
                              : _categories.Count == 0 ? "Category for Start run mode: none - no categories yet."
                              : "Category for Start run mode: none picked - pick one with < >.";
            _catStatusText.text = _catStatus;
        }

        /// The Runs tab's category rows; returns the new y.
        private float DrawCategories(float y, float w)
        {
            if (!Ctx.Run.Active)
            {
                if (GUI.Button(new Rect(0, y + 2, 28, 22), "<")) Pick(-1);
                if (GUI.Button(new Rect(32, y + 2, 28, 22), ">")) Pick(1);
                GUI.enabled = !_fetching;
                if (GUI.Button(new Rect(64, y + 2, 160, 22), "Check categories")) FetchCategories();
                GUI.enabled = true;
                y += 28f;
            }
            y += UiText.Draw(0, y, w, _catText) + 2f;
            if (!Ctx.Run.Active && _catStatusText.text.Length > 0) y += UiText.DrawDim(0, y, w, _catStatusText) + 4f;
            return y;
        }
    }
}
