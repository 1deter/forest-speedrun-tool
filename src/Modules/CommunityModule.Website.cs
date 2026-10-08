using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Website spots (author, 2026-10-05: "less manual downloading"): the
    // spots runners uploaded to forest.deter.cloud, listed in the Practice
    // tab's Import view; one click adds one read-only under the category
    // "Website", the way community entries are (Duplicate = own copy; the
    // runner's own ids win; the same id is the same original, so the
    // site's board - their best run - compares on it as before).
    //
    // Only what the site keeps: the segment, no start state, no attempts
    // (Data/SiteSpots). Written to segments/website.txt; nothing is
    // fetched unless the runner clicks.
    // ------------------------------------------------------------------
    public sealed partial class CommunityModule
    {
        public sealed class WebEntry
        {
            public SiteSpot Spot;
            public bool Added;
            public readonly GUIContent Label = new GUIContent("");
        }

        public readonly List<WebEntry> WebSpots = new List<WebEntry>();
        public string WebStatus = "";
        public bool WebBusy { get; private set; }

        private string WebBase
        {
            get
            {
                RunUploadModule up = Host.Find<RunUploadModule>();
                return up != null ? up.SiteUrl : null;
            }
        }

        public void WebRefresh()
        {
            if (WebBusy) return;
            string site = WebBase;
            if (string.IsNullOrEmpty(site)) { WebStatus = "No website address set (Settings)."; return; }
            WebBusy = true;
            WebStatus = "Asking the website...";
            Ctx.Runner.StartCoroutine(WebListRoutine(SiteSpots.ListUrl(site)));
        }

        private IEnumerator WebListRoutine(string url)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", url, null, null, null, 20f,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            WebBusy = false;
            List<SiteSpot> list = code == 200 ? SiteSpots.Parse(body) : null;
            if (list == null)
            {
                string why = code == 0 ? (error ?? "no answer") : "answer " + code;
                WebStatus = "Could not read the website (" + why + ").";
                Ctx.Log.LogWarning("Website spots: list: " + why + ".");
                yield break;
            }

            HashSet<string> have = WebIds();
            WebSpots.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                WebEntry e = new WebEntry();
                e.Spot = list[i];
                e.Added = have.Contains(e.Spot.Id);
                WebSpots.Add(e);
            }
            WebRelabel();
            WebStatus = list.Count == 0 ? "No runner spots on the website yet." : list.Count + " spot(s) on the website.";
            Ctx.Log.LogInfo("Website spots: " + list.Count + " listed.");
        }

        public void WebAdd(WebEntry entry)
        {
            if (WebBusy || entry == null) return;
            string site = WebBase;
            if (string.IsNullOrEmpty(site)) return;
            PracticeModule practice = Host.Find<PracticeModule>();
            if (practice != null && practice.OwnIds().Contains(entry.Spot.Id))
            {
                WebStatus = "'" + entry.Spot.Name + "' is already in your own list.";
                return;
            }
            WebBusy = true;
            WebStatus = "Fetching '" + entry.Spot.Name + "'...";
            Ctx.Runner.StartCoroutine(WebAddRoutine(entry, SiteSpots.FileUrl(site, entry.Spot.Id)));
        }

        private IEnumerator WebAddRoutine(WebEntry entry, string url)
        {
            long code = 0;
            string body = null, error = null;
            yield return Ctx.Runner.StartCoroutine(WebRequest.Send("GET", url, null, null, null, 30f,
                delegate(long c, string b, string e) { code = c; body = b; error = e; }));
            WebBusy = false;

            string perr = null;
            SegmentBundle bundle = code == 200 && body != null ? SegmentBundle.Parse(body, out perr, null) : null;
            if (bundle == null)
            {
                string why = code == 0 ? (error ?? "no answer") : code == 200 ? (perr ?? "not a spot file") : "answer " + code;
                WebStatus = "Could not fetch '" + entry.Spot.Name + "' (" + why + ").";
                Ctx.Log.LogWarning("Website spots: '" + entry.Spot.Id + "': " + why + ".");
                yield break;
            }
            if (!string.Equals(bundle.Segment.Id, entry.Spot.Id, StringComparison.OrdinalIgnoreCase))
            {
                WebStatus = "The website sent another spot than asked for - not added.";
                Ctx.Log.LogWarning("Website spots: asked for '" + entry.Spot.Id + "', got '" + bundle.Segment.Id + "'.");
                yield break;
            }

            bool update = entry.Added;
            List<Segment> all = WebRead();
            for (int i = all.Count - 1; i >= 0; i--)
                if (string.Equals(all[i].Id, entry.Spot.Id, StringComparison.OrdinalIgnoreCase)) all.RemoveAt(i);
            all.Add(bundle.Segment);
            string err = WebWrite(all);
            if (err != null) { WebStatus = "Not saved: " + err; yield break; }
            entry.Added = true;
            WebRelabel();
            string state = WebStartState(bundle);
            WebStatus = (update ? "Updated '" : "Added '") + entry.Spot.Name + "' under Website" +
                        (state == "start state" ? " with its start state." : state.Length > 0 ? " (" + state + ")." : ".");
            Ctx.Log.LogInfo("Website spots: " + (update ? "updated" : "added") + " '" + entry.Spot.Id + "'" +
                            (state.Length > 0 ? ", " + state : "") + ".");
        }

        /// The spot's start state from the website (T-0194): written as the
        /// segment's own, so a restart restores it like a community spot's.
        /// Without one, a left-over state that is not the route's goes (a
        /// state that is the route's - a community pack's - stays). Returns
        /// "start state", "" (none), or what went wrong.
        private string WebStartState(SegmentBundle bundle)
        {
            SavestateModule savestates = Host.Find<SavestateModule>();
            if (savestates == null) return "";
            Segment s = bundle.Segment;
            try
            {
                if (bundle.StartState == null)
                {
                    string have = savestates.ReadStartStateText(s);
                    if (have != null)
                    {
                        string perr;
                        SavestateFile f = SavestateFile.Parse(have, out perr);
                        if (s.StartState.Length > 0 && f != null && Segment.HashText(f.Data) == s.StartState) return "start state";
                        savestates.DeleteStartState(s);
                    }
                    return s.StartState.Length > 0 ? "no start state on the website yet - restarts teleport only" : "";
                }
                if (savestates.ReadStartStateText(s) == bundle.StartState) return "start state";
                string err = savestates.WriteStartStateText(s, bundle.StartState);
                return err == null ? "start state" : "start state not written: " + err;
            }
            catch (Exception ex) { return "start state failed: " + ex.Message; }
        }

        public void WebRemove(WebEntry entry)
        {
            if (WebBusy || entry == null) return;
            List<Segment> all = WebRead();
            int before = all.Count;
            for (int i = all.Count - 1; i >= 0; i--)
                if (string.Equals(all[i].Id, entry.Spot.Id, StringComparison.OrdinalIgnoreCase)) all.RemoveAt(i);
            if (all.Count == before) { entry.Added = false; WebRelabel(); return; }
            string err = WebWrite(all);
            if (err != null) { WebStatus = "Not saved: " + err; return; }
            entry.Added = false;
            WebRelabel();
            WebStatus = "Removed '" + entry.Spot.Name + "'.";
            Ctx.Log.LogInfo("Website spots: removed '" + entry.Spot.Id + "'.");
        }

        private void WebRelabel()
        {
            for (int i = 0; i < WebSpots.Count; i++)
            {
                SiteSpot s = WebSpots[i].Spot;
                StringBuilder sb = new StringBuilder(s.Name);
                if (s.By.Length > 0) sb.Append("  by ").Append(s.By);
                sb.Append("  -  ").Append(s.Runs).Append(s.Runs == 1 ? " run" : " runs");
                if (!float.IsNaN(s.Best)) sb.Append(", best ").Append(Format(s.Best));
                if (WebSpots[i].Added) sb.Append("  [added]");
                WebSpots[i].Label.text = sb.ToString();
            }
        }

        private static string Format(float seconds)
        {
            int m = (int)(seconds / 60f);
            return m + ":" + (seconds - m * 60f).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private string WebPath { get { return Path.Combine(_segmentsDir, SiteSpots.SegmentFile); } }

        private List<Segment> WebRead()
        {
            List<Segment> list = new List<Segment>();
            try
            {
                if (File.Exists(WebPath))
                    list.AddRange(SegmentFormat.ParseAll(File.ReadAllLines(WebPath, Encoding.UTF8), null));
            }
            catch (Exception ex) { Ctx.Log.LogWarning("Website spots: " + SiteSpots.SegmentFile + " unreadable: " + ex.Message); }
            return list;
        }

        private HashSet<string> WebIds()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Segment> all = WebRead();
            for (int i = 0; i < all.Count; i++) ids.Add(all[i].Id);
            return ids;
        }

        /// Writes the file (or deletes it when empty) and reloads it in the
        /// Practice tab; an error text, or null.
        private string WebWrite(List<Segment> all)
        {
            try
            {
                if (all.Count == 0) { if (File.Exists(WebPath)) File.Delete(WebPath); }
                else
                {
                    if (!Directory.Exists(_segmentsDir)) Directory.CreateDirectory(_segmentsDir);
                    File.WriteAllText(WebPath, SiteSpots.SegmentFileText(all), Encoding.UTF8);
                }
                PracticeModule practice = Host.Find<PracticeModule>();
                if (practice != null) practice.ReloadCommunity();
                return null;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Website spots: " + SiteSpots.SegmentFile + " not written: " + ex.Message);
                return ex.Message;
            }
        }
    }
}
