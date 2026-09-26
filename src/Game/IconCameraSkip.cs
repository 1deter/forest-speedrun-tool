using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The HUD's action-icon camera, skipped in frames with no icon (raw
    // FPS, Next up 6; a `[Performance]` switch in PerfPatches, on,
    // behaviour-preserving). `HudGui/.../ActionIconCamera` (depth 95,
    // perspective, far 40) draws the NGUI widgets under it - action icons
    // ("take", "light"...), the plane icon, the ranged hit target, the
    // translation overlay - and costs ~0.26 ms a frame whatever it draws
    // (Unity culls every renderer for each camera). Most frames it draws
    // nothing.
    //
    // WHEN TO DECIDE: NGUI switches its draw calls on and off in
    // UIPanel.LateUpdate, so "nothing to draw" is only known after every
    // LateUpdate. Measured (bridge, v0.24.117-118): a camera ENABLED in an
    // earlier camera's onPreCull is not rendered that frame (Unity lists
    // the frame's cameras up front), but one DISABLED there is skipped.
    // So the camera stays enabled, and in Camera_HUD's onPreCull (depth
    // 90, the camera just before it) it is disabled for this frame if
    // nothing it could draw is in its view; the end of the frame enables
    // it again.
    //
    // WHAT IT COULD DRAW: renderers on its layers inside its frustum -
    // NGUI's active draw calls (UIDrawCall.mActiveList; every NGUI widget
    // is drawn by one) and any other renderer under the camera itself
    // (none today; re-listed every 2 s). Anything unreadable = render.
    // The frustum test is Unity's own (TestPlanesAABB) on planes built
    // from the camera's matrices without allocating.
    //
    // A camera the game has switched off is never touched; one log line
    // when it starts.
    // ------------------------------------------------------------------
    public sealed class IconCameraSkip
    {
        private const string IconCameraName = "ActionIconCamera";
        private const string HudCameraName = "Camera_HUD";
        private const float ScanInterval = 2f;

        private readonly ManualLogSource _log;
        private FieldInfo _activeList, _buffer, _renderer;
        private Camera.CameraCallback _pre;
        private Action _end;
        private bool _on;
        private Camera _icons, _hud;
        private bool _skipped;
        private readonly Plane[] _planes = new Plane[6];
        private Renderer[] _own = new Renderer[0];
        private float _nextScan;
        private bool _announced;

        public IconCameraSkip(ManualLogSource log)
        {
            _log = log;
        }

        public string Apply()
        {
            Type dc = GameBridge.FindGameType("UIDrawCall");
            if (dc == null) return "UIDrawCall (NGUI) not found";
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic;
            _activeList = dc.GetField("mActiveList", any | BindingFlags.Static);
            _renderer = dc.GetField("mRenderer", any | BindingFlags.Instance);
            if (_activeList == null || _renderer == null) return "UIDrawCall.mActiveList / mRenderer not found";
            _buffer = _activeList.FieldType.GetField("buffer", any | BindingFlags.Instance);
            if (_buffer == null) return "BetterList.buffer not found";
            _on = true;
            _nextScan = 0f;
            _announced = false;
            _pre = OnPreCull;
            _end = OnEndOfFrame;
            Camera.onPreCull += _pre;
            FrameTimer.EndOfFrameHook += _end;
            return "";
        }

        public void Remove()
        {
            _on = false;
            if (_pre != null) Camera.onPreCull -= _pre;
            if (_end != null) FrameTimer.EndOfFrameHook -= _end;
            OnEndOfFrame();
            _icons = null;
            _hud = null;
        }

        /// Once a frame: finds the two cameras (a load brings new ones)
        /// and lists renderers under the icon camera, every 2 s.
        public void Tick()
        {
            if (!_on) return;
            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + ScanInterval;
            try
            {
                if (_icons == null || _hud == null)
                {
                    _icons = null;
                    _hud = null;
                    Camera[] cams = Camera.allCameras;
                    for (int i = 0; i < cams.Length; i++)
                    {
                        if (cams[i].name == IconCameraName) _icons = cams[i];
                        else if (cams[i].name == HudCameraName) _hud = cams[i];
                    }
                    if (_icons == null || _hud == null) return;
                    if (_hud.depth >= _icons.depth)
                    {
                        _log.LogInfo("Performance: action-icon camera left alone - the HUD camera does not render before it.");
                        _on = false;
                        return;
                    }
                }
                List<Renderer> own = new List<Renderer>();
                Renderer[] rs = _icons.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) own.Add(rs[i]);
                _own = own.ToArray();
                if (!_announced)
                {
                    _announced = true;
                    _log.LogInfo("Performance: action-icon camera skipped in frames with nothing to draw (decided in " + HudCameraName +
                                 "'s pre-cull; " + _own.Length + " renderer(s) under it besides NGUI's draw calls).");
                }
            }
            catch (Exception ex)
            {
                _icons = null;
                _log.LogWarning("Performance: action-icon camera scan failed: " + ex.Message);
            }
        }

        private void OnPreCull(Camera cam)
        {
            if (!_on || _icons == null || (object)cam != (object)_hud) return;
            try
            {
                if (!_icons.enabled || HasContent()) return;
                _icons.enabled = false;
                _skipped = true;
            }
            catch (Exception) { }
        }

        private void OnEndOfFrame()
        {
            if (!_skipped) return;
            _skipped = false;
            try { if (_icons != null) _icons.enabled = true; }
            catch (Exception) { }
        }

        // True unless nothing it could draw is in its view. Any doubt = true.
        private bool HasContent()
        {
            Camera c = _icons;
            int mask = c.cullingMask;
            SetPlanes(c.projectionMatrix * c.worldToCameraMatrix);

            object list = _activeList.GetValue(null);
            if (list == null) return true;
            object[] buf = _buffer.GetValue(list) as object[];
            if (buf == null) return true;
            for (int i = 0; i < buf.Length; i++)
            {
                Component dc = buf[i] as Component;
                if (dc == null) continue;
                Renderer r = _renderer.GetValue(dc) as Renderer;
                if (Visible(r, mask)) return true;
            }
            for (int i = 0; i < _own.Length; i++)
                if (Visible(_own[i], mask)) return true;
            return false;
        }

        private bool Visible(Renderer r, int mask)
        {
            if (r == null || !r.enabled) return false;
            GameObject go = r.gameObject;
            if (!go.activeInHierarchy || (mask & (1 << go.layer)) == 0) return false;
            return GeometryUtility.TestPlanesAABB(_planes, r.bounds);
        }

        // Gribb-Hartmann: the six planes of a view-projection matrix,
        // normals inward, as GeometryUtility.CalculateFrustumPlanes gives.
        private void SetPlanes(Matrix4x4 m)
        {
            SetPlane(0, m.m30 + m.m00, m.m31 + m.m01, m.m32 + m.m02, m.m33 + m.m03);   // left
            SetPlane(1, m.m30 - m.m00, m.m31 - m.m01, m.m32 - m.m02, m.m33 - m.m03);   // right
            SetPlane(2, m.m30 + m.m10, m.m31 + m.m11, m.m32 + m.m12, m.m33 + m.m13);   // bottom
            SetPlane(3, m.m30 - m.m10, m.m31 - m.m11, m.m32 - m.m12, m.m33 - m.m13);   // top
            SetPlane(4, m.m30 + m.m20, m.m31 + m.m21, m.m32 + m.m22, m.m33 + m.m23);   // near
            SetPlane(5, m.m30 - m.m20, m.m31 - m.m21, m.m32 - m.m22, m.m33 - m.m23);   // far
        }

        private void SetPlane(int i, float a, float b, float c, float d)
        {
            float len = Mathf.Sqrt(a * a + b * b + c * c);
            if (len < 1e-6f) len = 1e-6f;
            _planes[i] = new Plane(new Vector3(a / len, b / len, c / len), d / len);
        }
    }
}
