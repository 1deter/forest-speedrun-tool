using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Cameras that draw what nobody sees (raw FPS, Next up 6). Each camera
    // costs the main thread ~0.25 ms a frame whatever it draws - Unity
    // culls every renderer for it (~20k on the surface) - so these are
    // the cheapest frame time there is. Measured with Game/FrameTimer and
    // Game/RenderProbe (bridge, 2026-09-26, Slot 1). Two patches, each its
    // own `[Performance]` switch in PerfPatches, behaviour-preserving:
    //
    // 1. The terrain's grass camera. `_TerrainEtc_/AFSGrassDisplacementCamera`
    //    (a scene object) renders the displacement layer into a texture of
    //    its own, every frame, from a fixed spot with nothing in view. The
    //    grass reads the global `_AfsGrassDisplacementTex`, which
    //    AfsGrassDisplacementController.Update sets every frame to ITS
    //    camera's texture (`AFSGrassDisplacementCameraTest`, created at
    //    runtime, following the player). No loaded material has the
    //    scene camera's texture in any property. So the scene camera is a
    //    leftover: switched off (0.24-0.28 ms a frame), after checking
    //    that the controller's camera and the global are not it.
    //
    // 2. The endgame's plane screen. `Sections/ControlRoom/redcircles/
    //    Camera` renders a diorama (with post-processing) into 'EndPLane',
    //    which only `endPlaneCrashPrefab1/consoleDisplay` shows - its
    //    renderer is off until the end-crash ending. The game meant to
    //    switch the camera by distance (LOD_GroupToggle lists it), but
    //    LOD_GroupToggle's component switch has no case for a Camera, so
    //    it renders every frame while the endgame is loaded (0.35-0.40 ms).
    //    Here the camera is switched off and rendered on demand instead:
    //    a small behaviour on each renderer that shows the texture calls
    //    Camera.Render() from OnWillRenderObject - once a frame, only when
    //    the screen is about to be drawn, in that same frame. The picture
    //    is the one the game draws; the work is skipped while the screen
    //    is off or out of view.
    //
    // Both let go if the game switches the camera back on itself (not
    // seen - nothing in the game's code refers to either), and put
    // everything back when switched off. One log line per act.
    // ------------------------------------------------------------------
    public sealed class CameraTrim
    {
        private const float ScanInterval = 2f;
        private const string GrassCameraName = "AFSGrassDisplacementCamera";
        private const string ScreenTextureName = "EndPLane";

        private readonly ManualLogSource _log;
        private float _nextScan;

        public bool GrassOn { get; private set; }
        public bool ScreenOn { get; private set; }

        private Camera _grassOff;
        private Camera _screenCam;
        private readonly List<RenderOnView> _hooks = new List<RenderOnView>();
        private Type _controllerType;
        private FieldInfo _ctlCamera, _ctlTexture;
        private int _grassRefusedId, _screenRefusedId;
        private string _grassReadBy = "?";

        public CameraTrim(ManualLogSource log)
        {
            _log = log;
        }

        public string ApplyGrass()
        {
            _controllerType = GameBridge.FindGameType("AfsGrassDisplacementController");
            if (_controllerType == null) return "AfsGrassDisplacementController not found";
            _ctlCamera = _controllerType.GetField("DisplacementCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _ctlTexture = _controllerType.GetField("DisplacementTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_ctlCamera == null || _ctlTexture == null) return "the controller's camera / texture fields not found";
            GrassOn = true;
            _grassRefusedId = 0;
            _nextScan = 0f;
            return "";
        }

        public void RemoveGrass()
        {
            GrassOn = false;
            if (_grassOff != null)
            {
                _grassOff.enabled = true;
                _log.LogInfo("Performance: terrain grass camera back on (switch off).");
            }
            _grassOff = null;
        }

        public string ApplyScreen()
        {
            ScreenOn = true;
            _screenRefusedId = 0;
            _nextScan = 0f;
            return "";
        }

        public void RemoveScreen()
        {
            ScreenOn = false;
            ReleaseScreen("switch off");
        }

        /// Once a frame; looks for the cameras every 2 s (a load brings
        /// new ones).
        public void Tick()
        {
            if (!GrassOn && !ScreenOn) return;
            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + ScanInterval;
            try
            {
                if (GrassOn) ScanGrass();
                if (ScreenOn) ScanScreen();
            }
            catch (Exception ex)
            {
                _log.LogWarning("Performance: camera trim scan failed: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        private void ScanGrass()
        {
            if (_grassOff != null)
            {
                if (!_grassOff.enabled) return;
                _log.LogInfo("Performance: the game switched the terrain grass camera back on - left to it.");
                _grassRefusedId = _grassOff.GetInstanceID();
                _grassOff = null;
                return;
            }
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c == null || c.name != GrassCameraName || c.targetTexture == null) continue;
                if (c.GetInstanceID() == _grassRefusedId) continue;
                string why = GrassInUse(c);
                if (why.Length > 0)
                {
                    _grassRefusedId = c.GetInstanceID();
                    _log.LogInfo("Performance: terrain grass camera left on - " + why + ".");
                    continue;
                }
                c.enabled = false;
                _grassOff = c;
                _log.LogInfo("Performance: terrain grass camera off (" + Path(c.transform) + " drew into a texture nothing reads; " +
                             "the grass reads the one of '" + _grassReadBy + "').");
                return;
            }
        }

        // "" = the camera's texture is not the one the grass reads.
        private string GrassInUse(Camera c)
        {
            UnityEngine.Object ctl = UnityEngine.Object.FindObjectOfType(_controllerType);
            if (ctl == null) return "no grass controller to compare with";
            Camera used = _ctlCamera.GetValue(ctl) as Camera;
            Texture usedTex = _ctlTexture.GetValue(ctl) as Texture;
            if (used == null || usedTex == null) return "the grass controller has no camera yet";
            if (used == c) return "it is the controller's own camera";
            if (usedTex == c.targetTexture) return "the controller draws into its texture";
            if (Shader.GetGlobalTexture("_AfsGrassDisplacementTex") == c.targetTexture) return "the grass reads its texture";
            _grassReadBy = used.name;
            return "";
        }

        // ------------------------------------------------------------------
        private void ScanScreen()
        {
            if (_screenCam != null)
            {
                bool hooksAlive = true;
                for (int i = 0; i < _hooks.Count; i++) if (_hooks[i] == null) hooksAlive = false;
                if (!hooksAlive)
                {
                    ReleaseScreen("its screen was destroyed");
                    return;
                }
                if (!_screenCam.enabled) return;
                _screenRefusedId = _screenCam.GetInstanceID();
                ReleaseScreen("the game switched it back on - left to it");
                return;
            }
            // Destroyed with its scene (a load): start over.
            if (_hooks.Count > 0) ReleaseScreen("its scene was unloaded");

            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c == null || c.targetTexture == null || c.GetInstanceID() == _screenRefusedId) continue;
                if (c.targetTexture.name != ScreenTextureName) continue;
                RenderTexture rt = c.targetTexture;
                // The renderers that show it: under the same root, the texture as _MainTex.
                Renderer[] rs = c.transform.root.GetComponentsInChildren<Renderer>(true);
                List<Renderer> shows = new List<Renderer>();
                for (int k = 0; k < rs.Length; k++)
                {
                    Material[] ms = rs[k].sharedMaterials;
                    for (int m = 0; m < ms.Length; m++)
                        if (ms[m] != null && ms[m].HasProperty("_MainTex") && ms[m].mainTexture == rt) { shows.Add(rs[k]); break; }
                }
                if (shows.Count == 0)
                {
                    _screenRefusedId = c.GetInstanceID();
                    _log.LogInfo("Performance: endgame screen camera left on - no screen showing '" + ScreenTextureName + "' found.");
                    continue;
                }
                for (int k = 0; k < shows.Count; k++)
                {
                    RenderOnView hook = shows[k].gameObject.AddComponent<RenderOnView>();
                    hook.hideFlags = HideFlags.DontSave;
                    hook.Source = c;
                    _hooks.Add(hook);
                }
                c.enabled = false;
                _screenCam = c;
                _log.LogInfo("Performance: endgame screen camera (" + Path(c.transform) + ") renders only when its screen is drawn (" +
                             shows.Count + " screen(s): " + Path(shows[0].transform) + (shows[0].enabled && shows[0].gameObject.activeInHierarchy ? ", on" : ", off now") + ").");
                return;
            }
        }

        private void ReleaseScreen(string why)
        {
            for (int i = 0; i < _hooks.Count; i++)
                if (_hooks[i] != null) UnityEngine.Object.Destroy(_hooks[i]);
            bool had = _hooks.Count > 0 || _screenCam != null;
            _hooks.Clear();
            if (_screenCam != null) _screenCam.enabled = true;
            _screenCam = null;
            if (had) _log.LogInfo("Performance: endgame screen camera back to rendering every frame (" + why + ").");
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }

    /// On a renderer that shows a camera's texture: renders that camera
    /// once a frame, when this renderer is about to be drawn.
    public sealed class RenderOnView : MonoBehaviour
    {
        public Camera Source;
        // Several screens of one camera render it once a frame.
        private static int _lastId, _lastFrame = -1;

        private void OnWillRenderObject()
        {
            try
            {
                Camera src = Source;
                if (src == null || Camera.current == src) return;
                int id = src.GetInstanceID(), frame = Time.frameCount;
                if (id == _lastId && frame == _lastFrame) return;
                _lastId = id;
                _lastFrame = frame;
                src.Render();
            }
            catch (Exception) { }
        }
    }
}
