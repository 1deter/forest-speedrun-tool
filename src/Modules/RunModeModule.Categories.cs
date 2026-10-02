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
    // speedrun.com). Fetched a few seconds after startup and on "Check
    // categories", kept in config/ForestOverlay/categories.txt for offline
    // starts. A run spot's `run = ...` names its category (by id or name);
    // Start run mode uses the one picked in the Runs tab. The category goes
    // to Core/RunMode (what is locked, allowed, forced), the report (id +
    // version, the game) and the attempt's start on the site.
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule
    {
        private const string CategoriesFile = "categories.txt";
        private const float FetchDelay = 6f;

        private List<RunCategory> _categories = new List<RunCategory>();
        private ConfigEntry<string> _pickedCfg;
        private string _catStatus = "";
        private bool _fetching;
        private float _fetchAt = -1f;
        private readonly GUIContent _catText = new GUIContent("");
        private readonly GUIContent _catStatusText = new GUIContent("");
        private string _catBuiltFor;

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
        }

        private void FetchCategories()
        {
            string site = _upload != null ? _upload.SiteUrl : null;
            if (string.IsNullOrEmpty(site)) { _catStatus = "No site address set (Settings) - categories not checked."; return; }
            _fetching = true;
            _catStatus = "Checking the site's categories...";
            Ctx.Runner.StartCoroutine(Fetch(SiteProtocol.TrimUrl(site) + "/api/categories.txt"));
        }

        private IEnumerator Fetch(string url)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", url, null, null, null, 20f,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            _fetching = false;
            if (error != null || code != 200 || body == null)
            {
                _catStatus = "The site's categories could not be read (" + (error ?? "HTTP " + code) + ")" +
                             (_categories.Count > 0 ? " - using the " + _categories.Count + " from the last check." : ".");
                Ctx.Log.LogWarning("Run mode: categories not fetched from " + url + ": " + (error ?? "HTTP " + code) + ".");
                yield break;
            }
            List<RunCategory> list = RunCategory.Parse(body);
            _categories = list;
            _catStatus = list.Count == 0 ? "The site has no published categories yet." : list.Count + " categories from the site.";
            Ctx.Log.LogInfo("Run mode: " + list.Count + " categories from the site.");
            try { File.WriteAllText(Path.Combine(Ctx.ConfigDirectory, CategoriesFile), body, new UTF8Encoding(false)); }
            catch (Exception ex) { Ctx.Log.LogWarning("Run mode: categories not saved: " + ex.Message); }
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
            _catBuiltFor = null;
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
            if (allowed.Count > 0) sb.Append("Your choice: ").Append(string.Join(", ", allowed.ToArray())).Append(". ");
            sb.Append("Everything else is locked.");
            if (!c.AntiSplice) sb.Append(" No anti-splice code on screen.");
            if (c.Banned.Count > 0) sb.Append(" Banned: ").Append(string.Join("; ", c.Banned.ToArray())).Append('.');
            return sb.ToString();
        }

        private void RebuildCategoryText()
        {
            RunCategory shown = Ctx.Run.Active ? Ctx.Run.Category : Picked;
            string key = Ctx.Run.Active + "|" + (shown != null ? shown.Id + shown.Version : "-") + "|" + _categories.Count + "|" + _catStatus;
            if (key == _catBuiltFor) return;
            _catBuiltFor = key;
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
