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
    // Only what the site keeps: the segment, its start state, no attempts
    // (Data/SiteSpots). Written to segments/website.txt; nothing is
    // fetched unless the runner clicks.
    //
    // The runner's own spot (the site names its owner; T-0265) comes back
    // into their own list instead, editable, same id: their next upload on
    // it changes the website's copy. One already in their list that
    // differs from the website's can be replaced by it - a second click,
    // their start state and attempts kept (T-0218). Each row's answer
    // shows under it.
    // ------------------------------------------------------------------
    public sealed partial class CommunityModule
    {
        public sealed class WebEntry
        {
            public SiteSpot Spot;
            public bool Added;   // a read-only copy in website.txt
            public bool Mine;    // in the runner's own list (same id)
            public readonly GUIContent Label = new GUIContent("");
            /// The answer to the last click on this row, shown under it.
            public readonly GUIContent Message = new GUIContent("");
            /// The website's copy fetched by a first click, while Replace? waits.
            public SegmentBundle Fetched;
            public float ArmedUntil;
            public bool Armed { get { return Fetched != null && Time.unscaledTime <= ArmedUntil; } }
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
            PracticeModule practice = Host.Find<PracticeModule>();
            HashSet<string> own = practice != null ? practice.OwnIds() : new HashSet<string>();
            WebSpots.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                WebEntry e = new WebEntry();
                e.Spot = list[i];
                e.Added = have.Contains(e.Spot.Id);
                e.Mine = own.Contains(e.Spot.Id);
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
            Segment mine = practice != null ? practice.OwnById(entry.Spot.Id) : null;
            bool owner = practice != null && SiteSpots.IsOwner(entry.Spot, RunnerId());
            if (mine != null && !owner)
            {
                Say(entry, "'" + entry.Spot.Name + "' is already in your own list.");
                return;
            }
            if (mine != null && entry.Armed)
            {
                SegmentBundle fetched = entry.Fetched;
                entry.Fetched = null;
                TakeOwn(entry, fetched, mine);
                return;
            }
            entry.Fetched = null;
            WebBusy = true;
            Say(entry, "Fetching '" + entry.Spot.Name + "'...");
            Ctx.Runner.StartCoroutine(WebAddRoutine(entry, SiteSpots.FileUrl(site, entry.Spot.Id), owner));
        }

        private IEnumerator WebAddRoutine(WebEntry entry, string url, bool owner)
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
                Say(entry, "Could not fetch '" + entry.Spot.Name + "' (" + why + ").");
                Ctx.Log.LogWarning("Website spots: '" + entry.Spot.Id + "': " + why + ".");
                yield break;
            }
            if (!string.Equals(bundle.Segment.Id, entry.Spot.Id, StringComparison.OrdinalIgnoreCase))
            {
                Say(entry, "The website sent another spot than asked for - not added.");
                Ctx.Log.LogWarning("Website spots: asked for '" + entry.Spot.Id + "', got '" + bundle.Segment.Id + "'.");
                yield break;
            }
            if (owner) { OwnArrived(entry, bundle); yield break; }

            bool update = entry.Added;
            List<Segment> all = WebRead();
            for (int i = all.Count - 1; i >= 0; i--)
                if (string.Equals(all[i].Id, entry.Spot.Id, StringComparison.OrdinalIgnoreCase)) all.RemoveAt(i);
            all.Add(bundle.Segment);
            string err = WebWrite(all);
            if (err != null) { Say(entry, "Not saved: " + err); yield break; }
            entry.Added = true;
            WebRelabel();
            string state = WebStartState(bundle);
            Say(entry, (update ? "Updated '" : "Added '") + entry.Spot.Name + "' under Website" +
                       (state == "start state" ? " with its start state." : state.Length > 0 ? " (" + state + ")." : "."));
            Ctx.Log.LogInfo("Website spots: " + (update ? "updated" : "added") + " '" + entry.Spot.Id + "'" +
                            (state.Length > 0 ? ", " + state : "") + ".");
        }

        // --- the runner's own spot (T-0265 / T-0218) --------------------------------

        /// Who this install uploads as; null when unknown.
        private string RunnerId()
        {
            RunUploadModule up = Host.Find<RunUploadModule>();
            return up != null && up.RunnerIdNow != null ? up.RunnerIdNow() : null;
        }

        /// The website's copy of the runner's own spot arrived: back into
        /// their list when it is not there; when it is, the same says so and
        /// a different one waits for a second click (Replace?).
        private void OwnArrived(WebEntry entry, SegmentBundle bundle)
        {
            PracticeModule practice = Host.Find<PracticeModule>();
            if (practice == null) return;
            Segment mine = practice.OwnById(entry.Spot.Id);
            if (mine == null) { TakeOwn(entry, bundle, null); return; }
            entry.Mine = true;
            WebRelabel();
            if (SiteSpots.SameAsOwn(bundle.Segment, mine))
            {
                Say(entry, "Your '" + mine.Name + "' is the same as the website's - nothing to take.");
                Ctx.Log.LogInfo("Website spots: own '" + entry.Spot.Id + "' is the same as the website's.");
                return;
            }
            entry.Fetched = bundle;
            entry.ArmedUntil = Time.unscaledTime + 3f;
            Say(entry, "The website's '" + bundle.Segment.Name + "' differs from yours. Click Replace? within 3 s to take " +
                       "the website's (your start state and recorded attempts stay).");
            Ctx.Log.LogInfo("Website spots: own '" + entry.Spot.Id + "' differs from the website's - Replace? offered.");
        }

        /// The website's copy into the runner's own list, same id: `mine`
        /// (their entry, or null) is replaced keeping its start state; a
        /// spot added back takes the website's start state. A read-only
        /// copy of it in website.txt goes (the start state is the id's and
        /// stays).
        private void TakeOwn(WebEntry entry, SegmentBundle bundle, Segment mine)
        {
            PracticeModule practice = Host.Find<PracticeModule>();
            if (practice == null) return;
            Segment incoming = bundle.Segment;
            string state = mine != null ? KeepOwnStartState(incoming, mine, bundle.StartState) : WebStartState(bundle);

            List<Segment> all = WebRead();
            int before = all.Count;
            for (int i = all.Count - 1; i >= 0; i--)
                if (string.Equals(all[i].Id, incoming.Id, StringComparison.OrdinalIgnoreCase)) all.RemoveAt(i);
            string err = all.Count != before ? WebWrite(all) : null;
            if (err != null) { Say(entry, "Not saved: " + err); return; }

            err = practice.TakeOwnFromWebsite(incoming);
            entry.Added = false;
            entry.Mine = true;
            WebRelabel();
            string stateText = state.Length > 0 ? " (" + state + ")" : "";
            if (err != null) Say(entry, "Not saved: " + err + ".");
            else if (mine != null)
                Say(entry, "Replaced your '" + incoming.Name + "' with the website's" + stateText + ".");
            else
                Say(entry, "Added '" + incoming.Name + "' back to your own list" + stateText + ". It is yours to edit: " +
                           "your next finished run on it brings the changes to the website.");
            Ctx.Log.LogInfo("Website spots: own '" + incoming.Id + "' " + (mine != null ? "replaced by the website's" : "added back") +
                            (state.Length > 0 ? ", " + state : "") + (err != null ? " - not saved: " + err : "") + ".");
        }

        /// T-0218: the runner's start state stays - the file (kept by its
        /// id) and the route's hash; with no file here the website's is
        /// written only when it is the one the route was timed from.
        private string KeepOwnStartState(Segment incoming, Segment mine, string siteState)
        {
            incoming.StartState = mine.StartState;
            SavestateModule savestates = Host.Find<SavestateModule>();
            if (savestates == null) return "";
            try
            {
                if (savestates.ReadStartStateText(mine) != null) return "your start state kept";
                if (siteState != null && incoming.StartState.Length > 0)
                {
                    string perr;
                    SavestateFile f = SavestateFile.Parse(siteState, out perr);
                    if (f != null && Segment.HashText(f.Data) == incoming.StartState)
                    {
                        string err = savestates.WriteStartStateText(incoming, siteState);
                        return err == null ? "start state" : "start state not written: " + err;
                    }
                }
                return incoming.StartState.Length > 0 ? "no start state here - restarts teleport only" : "";
            }
            catch (Exception ex) { return "start state failed: " + ex.Message; }
        }

        private void Say(WebEntry entry, string text)
        {
            for (int i = 0; i < WebSpots.Count; i++)
                if (!ReferenceEquals(WebSpots[i], entry)) WebSpots[i].Message.text = "";
            entry.Message.text = text;
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
            bool hashed = s.StartState.Length > 0;   // DeleteStartState clears it
            try
            {
                if (bundle.StartState == null)
                {
                    string have = savestates.ReadStartStateText(s);
                    if (have != null)
                    {
                        string perr;
                        SavestateFile f = SavestateFile.Parse(have, out perr);
                        if (hashed && f != null && Segment.HashText(f.Data) == s.StartState) return "start state";
                        savestates.DeleteStartState(s);
                    }
                    return hashed ? "no start state on the website yet - restarts teleport only" : "";
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
            if (err != null) { Say(entry, "Not saved: " + err); return; }
            // Its start state goes with it - unless the id is in the
            // runner's own list too (theirs; kept by the same id).
            SavestateModule savestates = Host.Find<SavestateModule>();
            PracticeModule practice = Host.Find<PracticeModule>();
            Segment gone = new Segment();
            gone.Id = entry.Spot.Id;
            bool own = practice != null && practice.OwnById(gone.Id) != null;
            if (!own && savestates != null && savestates.HasStartState(gone)) savestates.DeleteStartState(gone);
            entry.Added = false;
            WebRelabel();
            Say(entry, "Removed '" + entry.Spot.Name + "'.");
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
                else if (WebSpots[i].Mine) sb.Append("  [in your list]");
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
