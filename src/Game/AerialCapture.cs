using ForestOverlay.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Top-down photographs of the world for the website's map (author,
    // 2026-09-27: "1:1 with the game so runners can plan routes"). Dev
    // only, driven from the bridge (Debug views module, AerialCapture):
    // the freecam's camera, orthographic and straight down, is placed over
    // one tile after another and the screen's centre square is saved -
    // once as the game looks ("canopy") and once with the trees' layer
    // culled ("ground": bushes, rocks, paths, trunks' shadows). Sea tiles are
    // captured too: skipping them left the map's coasts and corners empty.
    // The game's ocean does not come out in these frames (v0.24.170-177
    // took every coastal tile again with it switched off and got the same
    // picture; game-notes *Aerial capture*), so the website's water is drawn
    // by the bake from the terrain's heights (scripts/aerial-bake.py).
    //
    // What makes a tile look like the game (bridge, 2026-09-27; game-notes
    // *Aerial capture*):
    //  - Detail follows the player twice over: which LOD an object shows is
    //    measured from PlayerCamLocation.PlayerLoc (LOD_Settings.GetLOD; the
    //    script that writes it is paused by the freecam), and whether it is
    //    looked at at all follows the real player (with PlayerLoc alone, a
    //    tile 500 m from the player came out bare, the ground in its glossy
    //    far shading). So the player is moved to each tile's centre and
    //    PlayerLoc set there; he goes back where he stood at the end.
    //  - LOD ranges (trees mid 115 m, small bushes 60 m, pickups 80 m) come
    //    from LOD_Manager's RangeMultiplierPerQuality(Small) x an fps-based
    //    quality, recomputed every frame: the multipliers are raised and the
    //    fps scaling switched off while capturing, so a tile is even to its
    //    corners.
    //  - The atmosphere's fog (Visibility ~1 km) is overridden; the sun is
    //    held at TimeOfDay 320 (from the west, ~40 degrees up): overhead,
    //    the terrain's specular turns every slope to glare. Setting TimeOfDay
    //    alone is not enough: the sun's angle follows DelayedTimeOfDay, which
    //    catches up in eased steps, and the colours follow the running clock
    //    (tiles came out orange, blue, with long shadows). The game's own
    //    way, as its plane-crash cutscene does: OverrideLightingTimeOfDay +
    //    LightingTimeOfDayOverrideValue (the sun snapped every frame), and
    //    Animate off (the clock stopped).
    //  - The camera culls layers by distance (Default 85 m, pickups 100,
    //    small props 120, bushes 300, trees 487 - layerCullDistances,
    //    re-written by CullDistanceManager.Update every frame): set to 0 (=
    //    the far plane) after Update, right before each captured frame. The camera sits 400 m above the
    //    tile's highest ground: shadows are drawn within ~200 m of the camera
    //    only (QualitySettings.shadowDistance, re-set by the game every
    //    frame), so any lower and a tile's middle came out darker than its
    //    edges; from 400 m no tile has shadows and all are lit alike.
    //  - Cloud shadows drift over the ground (Sunshine.OvercastTexture): one
    //    tile of four came out darker, and fine when taken again. The
    //    game's own BlankOvercastTexture stands in while capturing.
    //  - The camera's post-processing profile: eye adaptation re-exposes
    //    each tile to its own content - forest lifted, snow and sand held
    //    down, the website's map in bands (author, 2026-10-01). Switching it
    //    off does not hold: PostProcessingBehaviour.OnGUI turns it straight
    //    back on (via Scion's own auto exposure for a frame). So it stays on
    //    with its luminance range clamped to one value (ExposureEv) and
    //    Fixed adaptation: one exposure for every tile. The vignette (darker
    //    corners) is off; colour grading and bloom stay (the game's look).
    //  - The weather (TheForest.World.WeatherSystem) rolls rain and clouds
    //    while a ~20 min capture runs: an overcast sky made every tile after
    //    it 1.6x darker (2026-10-01, twice). The rain is stopped (AllOff), the
    //    overcast set clear and the WeatherSystem switched off for the run;
    //    it is switched back on after (the sky stays clear until it rolls).
    //  - The HUD camera (HudGui) keeps running with an empty culling mask -
    //    never switched off (gotcha 58); the overlay's UI is hidden.
    //  - The player's state is in the frame: the camera flown is the game's
    //    own (MainCamNew), with its hurt / weather overlays. Hunger and
    //    thirst hurt the player in god mode too - screen blood at the edges
    //    of tile 13_12 (2026-10-01) - a cold player gets frost, low health
    //    greys the picture. Those overlays are switched off before every
    //    frame (the game switches them back on) and the player is kept fed,
    //    watered and rested on every tile (he ends the run that way).
    //  - Lakes switch their water for a flat black stand-in ("LakeFake")
    //    150 m from PlayerLoc, by LOD_GroupToggle's own distances (963 of
    //    them, not LOD_Manager's ranges): a lake near a tile's edge came out
    //    black and cut at the edge (Lakes 4 and 12, tiles 10_2 / 14_2). Every
    //    toggle's distances are scaled by rangeScale while capturing.
    //  - The far plane reaches below the terrain's lowest point: the
    //    sinkhole's floor and water (y -300 / -304) are models under a hole
    //    in the terrain, which bottoms out at y 0 - "60 m below the tile's
    //    lowest ground" drew the pit black.
    //  - The camera clears the tallest model too, not only the terrain: the
    //    south mountains past the terrain's edge are models reaching y ~1100,
    //    and a camera 400 m over the terrain (y ~650) sat inside them - the
    //    near plane cut away what was above it while that part still cast
    //    its shadow on the snow below (the "overlook's shadow", author
    //    2026-10-02). Renderers reaching above the lowest camera are listed
    //    once per run; a tile they overlap puts its camera 400 m over them.
    //
    // Everything changed is put back by Stop / the end of the run. The
    // player is left where he stands, in god mode while it runs.
    // ------------------------------------------------------------------
    public sealed class AerialCapture : MonoBehaviour
    {
        public static ManualLogSource Log;

        public const int TreeLayer = 11;
        public const float Sea = 41.5f;

        /// The deepest model seen from above: the sinkhole's water (y -304).
        public const float Deepest = -320f;

        /// The camera's hurt / weather overlays, kept off while capturing.
        private static readonly string[] Overlays = { "BleedBehavior", "Frost", "Grayscale", "WaterBlurEffect", "Blur" };

        public string Status = "idle";

        /// Skip tiles whose ground is all under the sea (the old behaviour).
        public bool SkipSea;

        /// The capture's one exposure: the eye adaptation's luminance range
        /// clamped to this (EV). -3.5 matches the game's daytime forest from
        /// above; snow comes out brighter than the game's adapted view.
        public float ExposureEv = -3.5f;
        public bool Running { get { return _run != null; } }

        /// Set by the module: hides / shows the overlay's own UI.
        public Action<bool> SetOverlayUi;

        /// Set by the module: moves the player (the bridge's tp).
        public Func<Vector3, bool> MovePlayer;
        public Func<Vector3> PlayerPosition;

        private Coroutine _run;
        private bool _stop;
        private Saved _saved;

        public string Begin(FreeCamBehaviour freeCam, float x0, float z0, float x1, float z1, float tile,
                            float settle, float rangeScale, float sunTime)
        {
            if (_run != null) return "already running - " + Status;
            if (freeCam == null || !freeCam.Active) return "freecam is off";
            if (Terrain.activeTerrain == null) return "no terrain";
            if (tile < 20f || x1 <= x0 || z1 <= z0) return "bad area";
            _stop = false;
            _run = StartCoroutine(Run(freeCam, x0, z0, x1, z1, tile, Mathf.Max(0.5f, settle), Mathf.Max(1f, rangeScale), sunTime));
            return "started";
        }

        public string Stop()
        {
            if (_run == null) return "not running";
            _stop = true;
            return "stopping after this tile";
        }

        private void OnDestroy() { try { Restore(); } catch (Exception ex) { Lifecycle.Fail("AerialCapture.OnDestroy", ex); } }

        private IEnumerator Run(FreeCamBehaviour freeCam, float x0, float z0, float x1, float z1, float tile,
                                float settle, float rangeScale, float sunTime)
        {
            Camera cam = freeCam.Camera;
            Terrain terrain = Terrain.activeTerrain;
            string dir = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "ForestOverlay"), "aerial");
            Directory.CreateDirectory(Path.Combine(dir, "canopy"));
            Directory.CreateDirectory(Path.Combine(dir, "ground"));

            int nx = Mathf.CeilToInt((x1 - x0) / tile), nz = Mathf.CeilToInt((z1 - z0) / tile);
            int px = Screen.height;
            Texture2D read = new Texture2D(px, px, TextureFormat.RGB24, false);
            StringBuilder index = new StringBuilder();
            index.Append("tile = ").Append(F(tile)).Append('\n')
                 .Append("origin = ").Append(F(x0)).Append(',').Append(F(z0)).Append('\n')
                 .Append("grid = ").Append(nx).Append(',').Append(nz).Append('\n')
                 .Append("px = ").Append(px).Append('\n');
            int done = 0, skipped = 0;
            float started = Time.realtimeSinceStartup;

            Apply(cam, tile, rangeScale, sunTime);
            List<Renderer> tall = TallRenderers(terrain);
            if (Log != null) Log.LogInfo("Aerial capture: " + tall.Count + " renderer(s) reach above the terrain's camera height");
            string sunOff = SunOff();
            if (sunOff != null && Log != null)
                Log.LogWarning("Aerial capture: " + sunOff + " - every tile will come out dark (cave / endgame lighting); walk or teleport out first");
            Vector3 home = PlayerPosition != null ? PlayerPosition() : Vector3.zero;
            try
            {
                // Settle the raised ranges and the held light before the first tile.
                yield return new WaitForSeconds(3f);

                for (int iz = 0; iz < nz && !_stop; iz++)
                {
                    for (int ix = 0; ix < nx && !_stop; ix++)
                    {
                        float cx = x0 + (ix + 0.5f) * tile, cz = z0 + (iz + 0.5f) * tile;
                        float lo, hi;
                        Heights(terrain, cx, cz, tile, out lo, out hi);
                        if (SkipSea && hi < Sea - 3f) { skipped++; continue; }   // open sea

                        float ground = terrain.SampleHeight(new Vector3(cx, 0f, cz)) + terrain.transform.position.y;
                        if (MovePlayer != null) MovePlayer(new Vector3(cx, Mathf.Max(ground, Sea) + 1f, cz));
                        SetPlayerLoc(new Vector3(cx, ground + 1.8f, cz));
                        string over;
                        float model = ModelTop(tall, cx, cz, tile, out over);
                        float top = Mathf.Max(hi, model) + 400f;
                        if (model > hi && Log != null)
                            Log.LogInfo("Aerial capture: tile " + ix + "_" + iz + " camera raised to y " + top.ToString("0") + " over '" + over + "'");
                        freeCam.Place(new Vector3(cx, top, cz), 90f, 0f);
                        cam.nearClipPlane = 1f;
                        cam.farClipPlane = top - Mathf.Min(lo, Deepest) + 60f;
                        HoldSun(sunTime);
                        KeepPlayerWell();
                        Status = "tile " + (ix + 1) + "," + (iz + 1) + " of " + nx + "x" + nz + " (" + done + " saved, " + skipped + " sea)";

                        float until = Time.realtimeSinceStartup + settle;
                        while (Time.realtimeSinceStartup < until)
                        {
                            HoldSun(sunTime);   // the clock runs
                            yield return null;
                        }
                        cam.layerCullDistances = new float[32];
                        KeepPlayerWell();
                        HoldOverlays();

                        cam.cullingMask = _saved.CullingMask;
                        freeCam.Place(new Vector3(cx, top, cz), 90f, 0f);   // nothing may have turned it
                        yield return new WaitForEndOfFrame();
                        Save(read, px, Path.Combine(Path.Combine(dir, "canopy"), ix + "_" + iz + ".jpg"));

                        cam.cullingMask = _saved.CullingMask & ~(1 << TreeLayer);
                        yield return null;
                        cam.layerCullDistances = new float[32];   // CullDistanceManager.Update re-set them
                        HoldOverlays();
                        freeCam.Place(new Vector3(cx, top, cz), 90f, 0f);
                        yield return new WaitForEndOfFrame();
                        Save(read, px, Path.Combine(Path.Combine(dir, "ground"), ix + "_" + iz + ".jpg"));
                        cam.cullingMask = _saved.CullingMask;

                        index.Append(ix).Append(',').Append(iz).Append('\n');
                        done++;
                        if (done % 10 == 0 && Log != null)
                            Log.LogInfo("Aerial capture: " + done + " tiles saved, " + skipped + " sea, " + Status + ", " + Weather());
                    }
                }
            }
            finally
            {
                if (MovePlayer != null && home != Vector3.zero) MovePlayer(home);
                File.WriteAllText(Path.Combine(dir, "tiles.txt"), index.ToString());
                UnityEngine.Object.Destroy(read);
                Restore();
                Status = (_stop ? "stopped: " : "done: ") + done + " tiles saved, " + skipped + " sea skipped, "
                    + (Time.realtimeSinceStartup - started).ToString("0") + " s -> " + dir;
                if (Log != null) Log.LogInfo("Aerial capture " + Status);
                _run = null;
            }
        }

        // --- the tile -------------------------------------------------------

        /// Lowest and highest ground (world y) under a tile, sampled 9 x 9.
        private static void Heights(Terrain t, float cx, float cz, float tile, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            float y0 = t.transform.position.y;
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 8; j++)
                {
                    float h = t.SampleHeight(new Vector3(cx + (i / 8f - 0.5f) * tile, 0f, cz + (j / 8f - 0.5f) * tile)) + y0;
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
        }

        /// Enabled renderers whose top is above the lowest camera (the sea
        /// plus 400 m), skipping anything wider than the island (sky, ocean).
        private static List<Renderer> TallRenderers(Terrain t)
        {
            List<Renderer> list = new List<Renderer>();
            float floor = Sea + 400f;
            float wide = t.terrainData.size.x * 1.5f;
            Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                if (b.max.y <= floor || b.size.x > wide || b.size.z > wide) continue;
                list.Add(r);
            }
            return list;
        }

        /// The highest top of the listed renderers over a tile (-infinity if
        /// none overlaps it), and the name of the one that sets it.
        private static float ModelTop(List<Renderer> tall, float cx, float cz, float tile, out string name)
        {
            float top = float.MinValue, half = tile * 0.5f;
            name = null;
            for (int i = 0; i < tall.Count; i++)
            {
                Renderer r = tall[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                if (b.max.x < cx - half || b.min.x > cx + half || b.max.z < cz - half || b.min.z > cz + half) continue;
                if (b.max.y > top) { top = b.max.y; name = r.name; }
            }
            return top;
        }

        /// The screen's centre square, as a JPEG. Called after
        /// WaitForEndOfFrame, when the frame is complete.
        private static void Save(Texture2D read, int px, string path)
        {
            int left = (Screen.width - px) / 2;
            read.ReadPixels(new Rect(left, 0, px, px), 0, 0, false);
            read.Apply(false);
            File.WriteAllBytes(path, read.EncodeToJPG(92));
        }

        // --- the game's settings, changed and put back ------------------------

        private sealed class Saved
        {
            public Camera Camera;
            public bool Orthographic;
            public float OrthoSize, Near, Far;
            public int CullingMask;
            public float[] CullDistances;
            public object Atmosphere;
            public bool OverrideVisibility, Animate, OverrideLighting;
            public float LightingValue, TimeOfDay;
            public float Visibility, FogStart;
            public object Lod;
            public bool FpsScaling;
            public float[] Ranges, RangesSmall;
            public float PixelError;
            public readonly List<KeyValuePair<Camera, int>> Hud = new List<KeyValuePair<Camera, int>>();
            public readonly List<KeyValuePair<object, bool>> PostEffects = new List<KeyValuePair<object, bool>>();
            public object EyeModel, EyeSettings;
            public Behaviour Weather;
            public bool WeatherWasOn;
            public UnityEngine.Object Sunshine;
            public object Overcast;
            public bool GodModeWasOn;
            public Vector3 PlayerLoc;
            public readonly List<KeyValuePair<Behaviour, bool>> Overlays = new List<KeyValuePair<Behaviour, bool>>();
            public readonly List<KeyValuePair<object, float>> LodDistances = new List<KeyValuePair<object, float>>();
        }

        private void Apply(Camera cam, float tile, float rangeScale, float sunTime)
        {
            Saved s = new Saved();
            s.Camera = cam;
            s.Orthographic = cam.orthographic; s.OrthoSize = cam.orthographicSize;
            s.Near = cam.nearClipPlane; s.Far = cam.farClipPlane; s.CullingMask = cam.cullingMask;
            s.CullDistances = cam.layerCullDistances;
            cam.orthographic = true;
            cam.orthographicSize = tile / 2f;
            cam.layerCullDistances = new float[32];

            s.Atmosphere = StaticGet("TheForestAtmosphere", "Instance");
            if (s.Atmosphere != null)
            {
                s.OverrideVisibility = (bool)Get(s.Atmosphere, "overrideVisibility");
                s.Visibility = (float)Get(s.Atmosphere, "Visibility");
                s.FogStart = (float)Get(s.Atmosphere, "FogStartDistance");
                s.Animate = (bool)Get(s.Atmosphere, "Animate");
                s.OverrideLighting = (bool)Get(s.Atmosphere, "OverrideLightingTimeOfDay");
                s.LightingValue = (float)Get(s.Atmosphere, "LightingTimeOfDayOverrideValue");
                s.TimeOfDay = (float)Get(s.Atmosphere, "TimeOfDay");
                Set(s.Atmosphere, "Animate", false);
                Set(s.Atmosphere, "OverrideLightingTimeOfDay", true);
                Set(s.Atmosphere, "LightingTimeOfDayOverrideValue", sunTime);
                Set(s.Atmosphere, "overrideVisibility", true);
                Set(s.Atmosphere, "Visibility", 100000f);
                Set(s.Atmosphere, "FogStartDistance", 5000f);
            }

            s.Lod = StaticGet("LOD_Manager", "Instance");
            if (s.Lod != null)
            {
                s.FpsScaling = (bool)Get(s.Lod, "FpsQualityScaling");
                Set(s.Lod, "FpsQualityScaling", false);
                float[] r = (float[])Get(s.Lod, "RangeMultiplierPerQuality");
                float[] rs = (float[])Get(s.Lod, "RangeMultiplierPerQualitySmall");
                s.Ranges = (float[])r.Clone();
                s.RangesSmall = (float[])rs.Clone();
                for (int i = 0; i < r.Length; i++) r[i] = s.Ranges[i] * rangeScale;
                for (int i = 0; i < rs.Length; i++) rs[i] = s.RangesSmall[i] * rangeScale;
            }

            // LOD_GroupToggle._levels[i].VisibleDistance (lakes' LakeFake, rocks,
            // cliffs): the game's worker thread compares them with PlayerLoc.
            Type toggle = GameBridge.FindGameType("LOD_GroupToggle");
            UnityEngine.Object[] toggles = toggle != null ? UnityEngine.Object.FindObjectsOfType(toggle) : new UnityEngine.Object[0];
            for (int i = 0; i < toggles.Length; i++)
            {
                Array levels = Get(toggles[i], "_levels") as Array;
                if (levels == null) continue;
                foreach (object level in levels)
                {
                    object d = level != null ? Get(level, "VisibleDistance") : null;
                    if (!(d is float)) continue;
                    s.LodDistances.Add(new KeyValuePair<object, float>(level, (float)d));
                    Set(level, "VisibleDistance", (float)d * rangeScale);
                }
            }

            for (int i = 0; i < Overlays.Length; i++)
            {
                Behaviour b = cam.GetComponent(Overlays[i]) as Behaviour;
                if (b != null) s.Overlays.Add(new KeyValuePair<Behaviour, bool>(b, b.enabled));
            }

            // PostProcessingBehaviour.profile.vignette.enabled off;
            // profile.eyeAdaptation.settings clamped to one exposure (a struct:
            // changed on a boxed copy and written back).
            Component post = cam.GetComponent("PostProcessingBehaviour");
            object profile = post != null ? Get(post, "profile") : null;
            string exposure = "exposure not held (no eye adaptation)";
            if (profile != null)
            {
                object vignette = Get(profile, "vignette");
                object on = vignette != null ? Get(vignette, "enabled") : null;
                if (on is bool)
                {
                    s.PostEffects.Add(new KeyValuePair<object, bool>(vignette, (bool)on));
                    Set(vignette, "enabled", false);
                }
                object eye = Get(profile, "eyeAdaptation");
                object settings = eye != null ? Get(eye, "settings") : null;
                if (settings != null)
                {
                    s.EyeModel = eye;
                    s.EyeSettings = settings;            // a boxed copy: the original values
                    object held = Get(eye, "settings");  // another copy to change
                    Set(held, "minLuminance", ExposureEv);
                    Set(held, "maxLuminance", ExposureEv);
                    object type = Get(held, "adaptationType");
                    if (type != null) Set(held, "adaptationType", Enum.Parse(type.GetType(), "Fixed"));
                    Set(eye, "settings", held);
                    exposure = "exposure held at EV " + F(ExposureEv);
                }
            }

            Type sunshine = GameBridge.FindGameType("Sunshine");
            s.Sunshine = sunshine != null ? UnityEngine.Object.FindObjectOfType(sunshine) : null;
            if (s.Sunshine != null)
            {
                s.Overcast = Get(s.Sunshine, "OvercastTexture");
                object blank = Get(s.Sunshine, "BlankOvercastTexture");
                if (blank != null) Set(s.Sunshine, "OvercastTexture", blank);
            }

            string weather = "weather not held (no WeatherSystem)";
            Behaviour w = StaticGet("TheForest.Utils.Scene", "WeatherSystem") as Behaviour;
            if (w != null)
            {
                try
                {
                    MethodInfo off = w.GetType().GetMethod("AllOff", Any);
                    if (off != null) off.Invoke(w, null);
                    object state = Get(w, "State");
                    if (state != null) Set(w, "State", Enum.Parse(state.GetType(), "Idle"));
                    foreach (string f in new[] { "CloudOvercastCurrentValue", "CloudOvercastTargetValue" }) Set(w, f, 0.1f);
                    foreach (string f in new[] { "VCloudCoverageCurrentValue", "VCloudCoverageTargetValue" }) Set(w, f, 0f);
                    s.Weather = w;
                    s.WeatherWasOn = w.enabled;
                    w.enabled = false;
                    weather = "weather held clear";
                }
                catch (Exception e) { weather = "weather not held: " + e.GetType().Name; }
            }

            Terrain t = Terrain.activeTerrain;
            s.PixelError = t.heightmapPixelError;
            t.heightmapPixelError = 1f;

            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                if (c == null || c.transform.root.name != "HudGui") continue;
                s.Hud.Add(new KeyValuePair<Camera, int>(c, c.cullingMask));
                c.cullingMask = 0;
            }

            s.GodModeWasOn = DeathHooks.IsGodMode();
            if (!s.GodModeWasOn) DeathHooks.SetGodMode(true);
            s.PlayerLoc = GetPlayerLoc();
            if (SetOverlayUi != null) SetOverlayUi(false);
            _saved = s;
            HoldSun(sunTime);
            HoldOverlays();
            KeepPlayerWell();
            if (Log != null)
                Log.LogInfo("Aerial capture: tile " + F(tile) + " m at " + Screen.height + " px, LOD ranges x" + F(rangeScale)
                    + " (+ " + s.LodDistances.Count + " LOD toggle distances of " + toggles.Length + " toggles), "
                    + s.Hud.Count + " HUD camera(s) emptied, " + s.PostEffects.Count + " post effect(s) off, "
                    + s.Overlays.Count + " hurt / weather overlay(s) held off, player kept well, " + exposure + ", " + weather + ", fog off, " + (s.Sunshine != null ? "cloud shadows off, " : "")
                    + "sun at " + F(sunTime));
        }

        private void Restore()
        {
            Saved s = _saved;
            _saved = null;
            if (s == null) return;
            try
            {
                if (s.Camera != null)
                {
                    s.Camera.orthographic = s.Orthographic; s.Camera.orthographicSize = s.OrthoSize;
                    s.Camera.nearClipPlane = s.Near; s.Camera.farClipPlane = s.Far; s.Camera.cullingMask = s.CullingMask;
                    if (s.CullDistances != null) s.Camera.layerCullDistances = s.CullDistances;
                }
                if (s.Atmosphere != null)
                {
                    Set(s.Atmosphere, "overrideVisibility", s.OverrideVisibility);
                    Set(s.Atmosphere, "OverrideLightingTimeOfDay", s.OverrideLighting);
                    Set(s.Atmosphere, "LightingTimeOfDayOverrideValue", s.LightingValue);
                    Set(s.Atmosphere, "TimeOfDay", s.TimeOfDay);
                    Set(s.Atmosphere, "Animate", s.Animate);
                    Set(s.Atmosphere, "Visibility", s.Visibility);
                    Set(s.Atmosphere, "FogStartDistance", s.FogStart);
                }
                if (s.Lod != null)
                {
                    Set(s.Lod, "FpsQualityScaling", s.FpsScaling);
                    float[] r = (float[])Get(s.Lod, "RangeMultiplierPerQuality");
                    float[] rs = (float[])Get(s.Lod, "RangeMultiplierPerQualitySmall");
                    Array.Copy(s.Ranges, r, Math.Min(r.Length, s.Ranges.Length));
                    Array.Copy(s.RangesSmall, rs, Math.Min(rs.Length, s.RangesSmall.Length));
                }
                if (Terrain.activeTerrain != null) Terrain.activeTerrain.heightmapPixelError = s.PixelError;
                if (s.Sunshine != null && s.Overcast != null) Set(s.Sunshine, "OvercastTexture", s.Overcast);
                for (int i = 0; i < s.PostEffects.Count; i++) Set(s.PostEffects[i].Key, "enabled", s.PostEffects[i].Value);
                if (s.EyeModel != null) Set(s.EyeModel, "settings", s.EyeSettings);
                if (s.Weather != null) s.Weather.enabled = s.WeatherWasOn;
                for (int i = 0; i < s.LodDistances.Count; i++) Set(s.LodDistances[i].Key, "VisibleDistance", s.LodDistances[i].Value);
                for (int i = 0; i < s.Overlays.Count; i++)
                    if (s.Overlays[i].Key != null) s.Overlays[i].Key.enabled = s.Overlays[i].Value;
                for (int i = 0; i < s.Hud.Count; i++)
                    if (s.Hud[i].Key != null) s.Hud[i].Key.cullingMask = s.Hud[i].Value;
                if (!s.GodModeWasOn) DeathHooks.SetGodMode(false);
                SetPlayerLoc(s.PlayerLoc);
                if (SetOverlayUi != null) SetOverlayUi(true);
            }
            catch (Exception e)
            {
                if (Log != null) Log.LogWarning("Aerial capture: restoring settings failed: " + e.Message);
            }
        }

        /// The weather on the progress line - held clear since v0.24.179; a
        /// line that shows rain or overcast means the hold failed.
        private static string Weather()
        {
            object w = StaticGet("TheForest.Utils.Scene", "WeatherSystem");
            if (w == null) return "weather unknown";
            return "weather " + Get(w, "State") + " / rain " + Get(w, "CurrentType")
                + ", overcast " + Fmt(Get(w, "CloudOvercastCurrentValue")) + ", cloud cover " + Fmt(Get(w, "VCloudCoverageCurrentValue"));
        }

        private static string Fmt(object v) { return v is float ? F((float)v) : "?"; }

        /// The camera's hurt / weather overlays off, right before a frame:
        /// the game switches them on again as the player's state asks.
        private void HoldOverlays()
        {
            Saved s = _saved;
            if (s == null) return;
            for (int i = 0; i < s.Overlays.Count; i++)
                if (s.Overlays[i].Key != null) s.Overlays[i].Key.enabled = false;
        }

        /// Fed, watered, rested and healthy, the screen's blood cleared: what
        /// hurts the player (god mode stops the death, not the hits) also
        /// shows on the game's camera.
        private static void KeepPlayerWell()
        {
            object stats = StaticGet("TheForest.Utils.LocalPlayer", "Stats");
            if (stats == null) return;
            Set(stats, "Fullness", 1f);
            Set(stats, "Thirst", 0f);
            Set(stats, "Health", 100f);
            Set(stats, "HealthTarget", 100f);
            Set(stats, "Stamina", 100f);
            Set(stats, "Energy", 100f);
            DeathHooks.ClearBlood();
        }

        private static void HoldSun(float sunTime)
        {
            object atmosphere = StaticGet("TheForestAtmosphere", "Instance");
            if (atmosphere != null) Set(atmosphere, "TimeOfDay", sunTime);
        }

        // --- reflection -----------------------------------------------------

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static Vector3 GetPlayerLoc()
        {
            object v = StaticGet("PlayerCamLocation", "PlayerLoc");
            return v is Vector3 ? (Vector3)v : Vector3.zero;
        }

        private static void SetPlayerLoc(Vector3 p)
        {
            Type t = GameBridge.FindGameType("PlayerCamLocation");
            FieldInfo f = t != null ? t.GetField("PlayerLoc", Any) : null;
            if (f != null) f.SetValue(null, p);
        }

        /// Why the sun is not lighting the world, or null when it is: a save
        /// loaded in the endgame and teleported out without the game's exit
        /// left TimeAndWeather/R10 (the sun's parent) off (fixed in v0.24.222).
        private static string SunOff()
        {
            object atmos = StaticGet("TheForestAtmosphere", "Instance");
            Light sun = atmos != null ? Get(atmos, "Sun") as Light : null;
            if (sun == null) return null;
            if (!sun.gameObject.activeInHierarchy) return "the sun is switched off ('" + ObjectProbe.PathOf(sun.transform) + "' inactive)";
            return null;
        }

        private static object StaticGet(string type, string member)
        {
            Type t = GameBridge.FindGameType(type);
            if (t == null) return null;
            FieldInfo f = t.GetField(member, Any);
            if (f != null) return f.GetValue(null);
            PropertyInfo p = t.GetProperty(member, Any);
            return p != null ? p.GetValue(null, null) : null;
        }

        private static object Get(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, Any);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = o.GetType().GetProperty(name, Any);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static void Set(object o, string name, object value)
        {
            FieldInfo f = o.GetType().GetField(name, Any);
            if (f != null) { f.SetValue(o, value); return; }
            PropertyInfo p = o.GetType().GetProperty(name, Any);
            if (p != null && p.CanWrite) p.SetValue(o, value, null);
        }

        private static string F(float v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
