using ForestOverlay.Core;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Debug views for theory testing: freecam, collider and trigger
    // volumes, global wireframe.
    //
    // None of these are provided by the game. TheForest.DebugConsole has
    // 256 commands but nothing for wireframe, triggers or a detached
    // camera (_follow follows a target, it does not detach), so these are
    // drawn and driven by the plugin - see Game/DebugDraw.cs.
    //
    // Freecam moves the VIEW, not the player, so it does not write game
    // state. It still holds the player still while active, which does, so
    // it is reported as practice.
    // ------------------------------------------------------------------
    public sealed class DebugViewModule : OverlayModule
    {
        public override string Id { get { return "debugview"; } }
        public override string DisplayName { get { return "Debug views"; } }
        public override bool HasPanel { get { return true; } }

        // Freecam drives itself from raw input, so the panel must not hold
        // the player-lock/cursor state that other panels want.
        public override bool WantsPlayerLock { get { return !_freeCamOn; } }

        private GameObject _host;
        private DebugDrawBehaviour _draw;
        private WireframeBehaviour _wireframe;
        private FreeCamBehaviour _freeCam;

        private bool _freeCamOn;
        private bool _wireOn;
        private float _radius = 30f;

        private Rect _windowRect;
        private bool _windowPlaced;
        private string _status = "";

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);

            // One hidden host object carries the behaviours. DontDestroyOnLoad
            // so a level change does not silently drop them.
            _host = new GameObject("ForestOverlay_Debug");
            _host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_host);

            _draw = _host.AddComponent<DebugDrawBehaviour>();
            _freeCam = _host.AddComponent<FreeCamBehaviour>();
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            // F1 is deliberately left unbound - it opens the game's own
            // developer console when that is enabled, and a runner who has
            // not rebound yet would get both.
            map.Add("panel.debugview", KeyCode.Insert, "Debug views panel", TogglePanel);
            map.Add("debug.freecam", KeyCode.KeypadMultiply, "Toggle freecam", ToggleFreeCam);
        }

        public override void Tick()
        {
            if (_draw != null)
            {
                _draw.Origin = Ctx.Player.Transform;
                _draw.Radius = _radius;
            }

            // The wireframe hook has to live on whichever camera is
            // actually rendering, and that changes when freecam starts.
            if (_wireOn) AttachWireframe();
        }

        // ------------------------------------------------------------------
        private void ToggleFreeCam()
        {
            if (_freeCam == null) return;

            if (_freeCamOn)
            {
                _freeCam.End();
                _freeCamOn = false;
                _status = "freecam off";
            }
            else
            {
                Camera cam = Camera.main;
                if (cam == null) { _status = "no main camera"; return; }

                _freeCam.Begin(cam);
                _freeCamOn = true;

                // Hold the player so the body does not wander off while the
                // view is detached. This writes game state.
                if (Ctx.Bridge != null)
                {
                    Ctx.Bridge.SetPlayerLocked(true);
                    Ctx.Practice.Mark("freecam");
                }

                _status = "freecam on - WASD, QE up/down, Shift fast, Ctrl slow";
            }
        }

        private void AttachWireframe()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (_wireframe != null && _wireframe.gameObject == cam.gameObject)
            {
                _wireframe.Enabled = _wireOn;
                return;
            }

            if (_wireframe != null) _wireframe.Enabled = false;

            _wireframe = cam.gameObject.GetComponent<WireframeBehaviour>();
            if (_wireframe == null) _wireframe = cam.gameObject.AddComponent<WireframeBehaviour>();
            _wireframe.Enabled = _wireOn;
        }

        private void SetWireframe(bool on)
        {
            _wireOn = on;
            if (on) AttachWireframe();
            else if (_wireframe != null) _wireframe.Enabled = false;
        }

        // ------------------------------------------------------------------
        public override void ContributeHud(HudBuilder hud)
        {
            if (_freeCamOn) hud.Pair("Cam", "FREECAM");
        }

        public override void DrawPanel(int windowId)
        {
            if (!_windowPlaced)
            {
                _windowRect = new Rect(Screen.width - 430f, 540f, 400f, 260f);
                _windowPlaced = true;
            }

            _windowRect = GUI.Window(windowId, _windowRect, DrawContents, "Debug views");
        }

        private void DrawContents(int id)
        {
            float w = _windowRect.width;

            bool freecam = GUI.Toggle(new Rect(12, 28, 180, 22), _freeCamOn, " Freecam");
            if (freecam != _freeCamOn) ToggleFreeCam();

            bool wire = GUI.Toggle(new Rect(200, 28, 180, 22), _wireOn, " Wireframe");
            if (wire != _wireOn) SetWireframe(wire);

            bool cols = GUI.Toggle(new Rect(12, 54, 180, 22), _draw.ShowColliders, " Colliders");
            if (cols != _draw.ShowColliders) _draw.ShowColliders = cols;

            bool trigs = GUI.Toggle(new Rect(200, 54, 180, 22), _draw.ShowTriggers, " Triggers");
            if (trigs != _draw.ShowTriggers) _draw.ShowTriggers = trigs;

            GUI.Label(new Rect(12, 82, 120, 20), "Radius " + _radius.ToString("F0") + "m");
            _radius = GUI.HorizontalSlider(new Rect(130, 88, w - 150, 20), _radius, 5f, 120f);

            GUI.Label(new Rect(12, 110, w - 24, 20),
                      "Green = solid collider, orange = trigger volume.");
            GUI.Label(new Rect(12, 130, w - 24, 20),
                      "Volumes are world-space bounds, not exact mesh shapes.");

            GUI.Label(new Rect(12, 158, w - 24, 20), _status);

            GUI.Label(new Rect(12, 184, w - 24, 20),
                      "Freecam: WASD move, Q/E down/up, Shift fast, Ctrl slow.");
            GUI.Label(new Rect(12, 204, w - 24, 20),
                      "Wireframe covers everything the camera draws.");

            GUI.DragWindow(new Rect(0, 0, w, 22));
        }

        public override void Shutdown()
        {
            if (_freeCam != null) _freeCam.End();
            if (_wireframe != null) _wireframe.Enabled = false;
            if (_host != null) Object.Destroy(_host);
        }
    }
}
