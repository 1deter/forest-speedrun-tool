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
    // Community packs (Data/CommunityIndex): shared spots and timed
    // segments every runner gets, so a new runner starts with good spots
    // and practice ready (author, 2026-09-25).
    //
    // A few seconds after startup (and on "Check community now" in the
    // Practice tab's Import view) it fetches the repo's community/index.txt
    // from raw.githubusercontent.com - plain downloads, not the GitHub API,
    // so the 60-an-hour limit the update check shares is never touched -
    // downloads the .foseg files whose hash changed into
    // config/ForestOverlay/community/, drops ones no longer listed, writes
    // their segments to segments/community.txt ("Community", read-only in
    // the editor) and their start states beside the runner's own. The
    // runner's ids always win: a pack entry with an id they already have
    // is skipped. Attempts in a pack are not imported (they are someone's
    // times, not spots).
    //
    // WHY its own download code, not UpdateChecker's: the updater is the
    // one path that must never break, so it is not refactored for this.
    // ------------------------------------------------------------------
    public sealed class CommunityModule : OverlayModule
    {
        public const string DefaultUrl = "https://raw.githubusercontent.com/1deter/forest-speedrun-tool/main/community/";
        private const float StartupDelay = 5f;
        private const int MaxBytes = 20 * 1024 * 1024;

        public override string Id { get { return "community"; } }
        public override string DisplayName { get { return "Community spots"; } }

        private ConfigEntry<bool> _auto;
        private ConfigEntry<string> _url;
        private string _cache;
        private string _segmentsDir;
        private bool _startupDone;

        public string Status { get; private set; }
        public bool Busy { get; private set; }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _auto = ctx.Config.Bind("Community", "UpdateOnStartup", true,
                                    "Fetch the community spots / segments a few seconds after the game starts.");
            _url = ctx.Config.Bind("Community", "Url", DefaultUrl,
                                   "Folder holding the community index.txt and .foseg files (ends with /).");
            _cache = Path.Combine(ctx.ConfigDirectory, "community");
            _segmentsDir = Path.Combine(ctx.ConfigDirectory, "segments");
            Status = _auto.Value ? "checking shortly after startup" : "not checked (UpdateOnStartup is off) - Check community now";
        }

        public override void Tick()
        {
            if (_startupDone || Time.unscaledTime < StartupDelay) return;
            _startupDone = true;
            if (_auto.Value) CheckNow();
        }

        public void CheckNow()
        {
            if (Busy) return;
            Busy = true;
            Status = "checking...";
            Ctx.Runner.StartCoroutine(Check());
        }

        private IEnumerator Check()
        {
            string baseUrl = _url.Value.EndsWith("/") ? _url.Value : _url.Value + "/";
            byte[] indexBytes = null;
            string error = null;
            yield return Ctx.Runner.StartCoroutine(Fetch(baseUrl + CommunityIndex.IndexFile,
                                                         delegate(byte[] b, string e) { indexBytes = b; error = e; }));
            if (indexBytes == null)
            {
                Finish("index not reachable (" + error + ") - kept what was downloaded before", true);
                yield break;
            }

            int bad;
            List<CommunityIndex.Entry> index = CommunityIndex.Parse(Encoding.UTF8.GetString(indexBytes), out bad);

            List<string> cached = new List<string>();
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(_cache)) Directory.CreateDirectory(_cache);
                foreach (string path in Directory.GetFiles(_cache, "*" + SegmentBundle.Extension))
                {
                    string name = Path.GetFileName(path);
                    cached.Add(name);
                    hashes[name] = CommunityIndex.HashOf(File.ReadAllText(path, Encoding.UTF8));
                }
            }
            catch (Exception ex)
            {
                Finish("the cache folder is unreadable (" + ex.Message + ")", true);
                yield break;
            }

            List<CommunityIndex.Entry> fetch = new List<CommunityIndex.Entry>();
            List<string> drop = new List<string>();
            CommunityIndex.Plan(index, cached, delegate(string f) { string h; return hashes.TryGetValue(f, out h) ? h : null; }, fetch, drop);

            int got = 0, failed = 0;
            for (int i = 0; i < fetch.Count; i++)
            {
                CommunityIndex.Entry e = fetch[i];
                byte[] data = null;
                string why = null;
                yield return Ctx.Runner.StartCoroutine(Fetch(baseUrl + e.File, delegate(byte[] b, string err) { data = b; why = err; }));
                string text = data != null ? Encoding.UTF8.GetString(data) : null;
                string perr = null;
                if (text != null && CommunityIndex.HashOf(text) != e.Hash) why = "hash differs from the index (the server may still be updating)";
                else if (text != null && SegmentBundle.Parse(text, out perr, null) == null) why = perr;
                if (text == null || why != null)
                {
                    failed++;
                    Ctx.Log.LogWarning("Community: " + e.File + " not updated: " + (why ?? "no data"));
                    continue;
                }
                try { File.WriteAllText(Path.Combine(_cache, e.File), text, Encoding.UTF8); got++; }
                catch (Exception ex) { failed++; Ctx.Log.LogWarning("Community: " + e.File + " not written: " + ex.Message); }
            }

            // Dropped packs: their start states go with them (only ids no
            // listed pack still uses, and never a runner's own).
            List<string> droppedIds = new List<string>();
            for (int i = 0; i < drop.Count; i++)
            {
                try
                {
                    string path = Path.Combine(_cache, drop[i]);
                    string perr;
                    SegmentBundle b = SegmentBundle.Parse(File.ReadAllText(path, Encoding.UTF8), out perr, null);
                    if (b != null) droppedIds.Add(b.Segment.Id);
                    File.Delete(path);
                }
                catch (Exception ex) { Ctx.Log.LogWarning("Community: " + drop[i] + " not removed: " + ex.Message); }
            }

            string applied = Apply(index, droppedIds);
            Finish(index.Count + " pack(s) listed, " + got + " downloaded, " + drop.Count + " removed" +
                   (failed > 0 ? ", " + failed + " failed (see the log)" : "") +
                   (bad > 0 ? ", " + bad + " bad index line(s)" : "") + " | " + applied, false);
        }

        /// Rewrites community.txt and the packs' start states from the
        /// cache; tells the Practice tab to reload that file.
        private string Apply(List<CommunityIndex.Entry> index, List<string> droppedIds)
        {
            PracticeModule practice = Host.Find<PracticeModule>();
            SavestateModule savestates = Host.Find<SavestateModule>();
            if (practice == null) return "no Practice tab";

            List<SegmentBundle> bundles = new List<SegmentBundle>();
            for (int i = 0; i < index.Count; i++)
            {
                string path = Path.Combine(_cache, index[i].File);
                if (!File.Exists(path)) continue;
                try
                {
                    string perr;
                    SegmentBundle b = SegmentBundle.Parse(File.ReadAllText(path, Encoding.UTF8), out perr, null);
                    if (b != null) bundles.Add(b);
                }
                catch (Exception ex) { Ctx.Log.LogWarning("Community: " + index[i].File + " unreadable: " + ex.Message); }
            }

            HashSet<string> taken = practice.OwnIds();
            List<string> skipped = new List<string>();
            string text = CommunityIndex.SegmentFileText(bundles, taken, skipped);

            int states = 0;
            if (savestates != null)
            {
                for (int i = 0; i < bundles.Count; i++)
                {
                    Segment s = bundles[i].Segment;
                    if (taken.Contains(s.Id)) continue;
                    try
                    {
                        if (bundles[i].StartState == null)
                        {
                            if (savestates.HasStartState(s)) savestates.DeleteStartState(s);
                            continue;
                        }
                        string have = savestates.ReadStartStateText(s);
                        if (have == bundles[i].StartState) continue;
                        string err = savestates.WriteStartStateText(s, bundles[i].StartState);
                        if (err == null) states++;
                        else Ctx.Log.LogWarning("Community: start state of '" + s.Id + "' not written: " + err);
                    }
                    catch (Exception ex) { Ctx.Log.LogWarning("Community: start state of '" + s.Id + "' failed: " + ex.Message); }
                }
                for (int i = 0; i < droppedIds.Count; i++)
                {
                    Segment gone = new Segment();
                    gone.Id = droppedIds[i];
                    bool stillListed = false;
                    for (int j = 0; j < bundles.Count; j++)
                        if (string.Equals(bundles[j].Segment.Id, gone.Id, StringComparison.OrdinalIgnoreCase)) stillListed = true;
                    if (!stillListed && !taken.Contains(gone.Id) && savestates.HasStartState(gone)) savestates.DeleteStartState(gone);
                }
            }

            try
            {
                string path = Path.Combine(_segmentsDir, CommunityIndex.SegmentFile);
                bool empty = bundles.Count == skipped.Count;
                if (empty) { if (File.Exists(path)) File.Delete(path); }
                else
                {
                    if (!Directory.Exists(_segmentsDir)) Directory.CreateDirectory(_segmentsDir);
                    File.WriteAllText(path, text, Encoding.UTF8);
                }
                practice.ReloadCommunity();
            }
            catch (Exception ex)
            {
                return "community.txt not written (" + ex.Message + ")";
            }

            if (skipped.Count > 0)
                Ctx.Log.LogInfo("Community: skipped (you have that id): " + string.Join(", ", skipped.ToArray()) + ".");
            return (bundles.Count - skipped.Count) + " spot(s) in the list" +
                   (states > 0 ? ", " + states + " start state(s) written" : "") +
                   (skipped.Count > 0 ? ", " + skipped.Count + " skipped (you have that id)" : "");
        }

        private void Finish(string status, bool warn)
        {
            Status = status + " (" + DateTime.Now.ToString("HH:mm") + ")";
            Busy = false;
            if (warn) Ctx.Log.LogWarning("Community: " + status + ".");
            else Ctx.Log.LogInfo("Community: " + status + ".");
        }

        // A plain GET through Unity's UnityWebRequest, by reflection as in
        // UpdateChecker (Mono's own TLS cannot reach GitHub). Unity 5.6
        // does not flag a 404 as an error: the code is checked here.
        private IEnumerator Fetch(string url, Action<byte[], string> done)
        {
            Type requestType = Type.GetType("UnityEngine.Networking.UnityWebRequest, UnityEngine");
            if (requestType == null) { done(null, "UnityWebRequest unavailable"); yield break; }

            object request;
            try
            {
                request = requestType.GetMethod("Get", new Type[] { typeof(string) }).Invoke(null, new object[] { url });
                System.Reflection.MethodInfo setHeader = requestType.GetMethod("SetRequestHeader");
                if (setHeader != null) setHeader.Invoke(request, new object[] { "User-Agent", "ForestOverlay" });
                System.Reflection.MethodInfo send = requestType.GetMethod("Send") ?? requestType.GetMethod("SendWebRequest");
                send.Invoke(request, null);
            }
            catch (Exception ex) { done(null, ex.Message); yield break; }

            System.Reflection.PropertyInfo isDone = requestType.GetProperty("isDone");
            float timeout = Time.unscaledTime + 30f;
            while (!(bool)isDone.GetValue(request, null))
            {
                if (Time.unscaledTime > timeout) { done(null, "timed out"); yield break; }
                yield return null;
            }

            byte[] data = null;
            string error = null;
            try
            {
                System.Reflection.PropertyInfo errorProp = requestType.GetProperty("error");
                error = errorProp != null ? errorProp.GetValue(request, null) as string : null;
                long code = 0;
                System.Reflection.PropertyInfo codeProp = requestType.GetProperty("responseCode");
                if (codeProp != null) code = Convert.ToInt64(codeProp.GetValue(request, null));
                if (string.IsNullOrEmpty(error) && code != 200 && code != 0) error = "HTTP " + code;
                if (string.IsNullOrEmpty(error))
                {
                    object handler = requestType.GetProperty("downloadHandler").GetValue(request, null);
                    data = handler.GetType().GetProperty("data").GetValue(handler, null) as byte[];
                    if (data == null) error = "empty response";
                    else if (data.Length > MaxBytes) { data = null; error = "larger than " + (MaxBytes >> 20) + " MB"; }
                }
            }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                try { requestType.GetMethod("Dispose").Invoke(request, null); }
                catch (Exception) { }
            }
            done(string.IsNullOrEmpty(error) ? data : null, error);
        }
    }
}
