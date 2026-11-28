using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // The zone editor (T-0492; decisions.md *Zone editor mode*): a mode of
    // its own, opened from a timed segment in the Practice tab, in which the
    // zones are drawn in the world instead of typed - Momentum Mod's zone
    // editor is the model.
    //
    //   * The view flies free (its own FreeCamBehaviour; the body is held,
    //     as freecam holds it). A crosshair ray picks the ground.
    //   * A box is three clicks: two ground corners (width and length),
    //     then the height - the view ray on the upright plane through the
    //     footprint's middle. Data/ZoneBuilder does the geometry.
    //   * The grid snaps x / z (G cycles off, 0.5, 1, 2, 4 m; R / Shift+R
    //     turn it 15 degrees - the box takes the grid's turn).
    //   * Tab picks what the box becomes: the start, a checkpoint, a new
    //     checkpoint or the end. The segment's zones show in their colours
    //     meanwhile (Game/ZonePreview, the Practice selection).
    //   * Backspace takes back a click, Delete removes the picked
    //     checkpoint, Enter or Esc leaves. The window closes on entering.
    //
    // Writes game state (the body held) and edits the segment: practice
    // only, marked; run mode refuses it.
    // ------------------------------------------------------------------
    public sealed class ZoneEditorModule : OverlayModule
    {
        public override string Id { get { return "zoneeditor"; } }
        public override string DisplayName { get { return "Zone editor"; } }
        public override bool IsPracticeOnly { get { return true; } }
        public override bool HoldsPlayer { get { return _active; } }

        private const float RayLength = 1500f;
        private const float GridStep = 15f;

        private GameObject _host;
        private FreeCamBehaviour _cam;
        private ZoneEditorDraw _draw;
        private PracticeModule _practice;

        private bool _active;
        private Segment _segment;
        private int _target;                 // -1 start, 0..n-1 a checkpoint, n a new checkpoint, n + 1 the end
        private int _clicks;                 // 0, 1 (corner A set), 2 (corners set: height next)
        private Vector3 _a, _b;
        private int _grid = 2;               // ZoneBuilder.GridSizes index: 1 m
        private float _gridYaw;
        private bool _hit;
        private Vector3 _point;
        private bool _helpDirty = true;
        private readonly GUIContent _help = new GUIContent("");

        public bool Active { get { return _active; } }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _host = new GameObject("ForestOverlay_ZoneEditor");
            _host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_host);
            _cam = _host.AddComponent<FreeCamBehaviour>();
            _cam.Owner = "Zone editor";
            _draw = _host.AddComponent<ZoneEditorDraw>();
        }

        // --- entering and leaving -------------------------------------------

        /// The Practice tab's "Edit zones" on a timed segment. Null, or why
        /// not (said where the click was).
        public string Enter(Segment s)
        {
            if (_active) { Leave("Edit zones again"); return null; }
            if (s == null || !s.IsTimed) return "Pick a timed segment first.";
            if (SegmentLibrary.IsCommunity(s)) return "Community spots are read-only - Duplicate it to edit its zones.";
            if (Ctx.Run.Refuse("zone editor", "the zone editor")) return Ctx.Run.RefusedText("The zone editor");
            if (!Ctx.Player.Found) return "No player - load a game first.";
            Camera main = Camera.main;
            if (main == null) return "No camera to fly.";
            if (!_cam.Begin(main)) return "The zone editor could not take the view: " + _cam.LastReport + ".";

            if (_practice == null) _practice = Host.Find<PracticeModule>();
            _segment = s;
            _active = true;
            _target = -1;
            _clicks = 0;
            _helpDirty = true;
            Ctx.Practice.Mark("zone editor");
            Host.CloseAllPanels();
            Ctx.Log.LogInfo("Zone editor: on for '" + s.Name + "' (" + s.Id + ").");
            return null;
        }

        private void Leave(string why)
        {
            if (!_active) return;
            _active = false;
            _clicks = 0;
            _cam.End();
            _draw.Show = false;
            Ctx.Log.LogInfo("Zone editor: off (" + why + ").");
            Ctx.Notice.Show("Zone editor closed - the zones are saved with the segment.", 3f);
            _segment = null;
        }

        public override void Shutdown()
        {
            if (_active) Leave("the game closed");
            if (_host != null) Object.Destroy(_host);
        }

        // --- every frame ------------------------------------------------------

        public override void Tick()
        {
            if (!_active) return;
            // The camera torn down under us (a scene load), the segment
            // deleted, or the player gone: nothing left to edit.
            if (!_cam.Active) { _active = false; _draw.Show = false; Ctx.Log.LogInfo("Zone editor: off (the camera changed)."); return; }
            if (!Ctx.Player.Found || _practice == null || !_practice.Holds(_segment)) { Leave("the segment or the player went away"); return; }

            bool input = !Host.AnyPanelOpen();
            _cam.InputEnabled = input;
            Aim();
            if (input) Keys();
            Preview();
        }

        private void Aim()
        {
            Transform t = _cam.Camera.transform;
            RaycastHit h;
            _hit = Physics.Raycast(t.position, t.forward, out h, RayLength, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (_hit) _point = ZoneBuilder.Snap(h.point, ZoneBuilder.GridSizes[_grid], _gridYaw);
        }

        private void Keys()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                Leave("done");
                return;
            }
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                int n = _segment.Checkpoints.Count;
                _target = Input.GetKey(KeyCode.LeftShift) ? (_target <= -1 ? n + 1 : _target - 1)
                                                          : (_target >= n + 1 ? -1 : _target + 1);
                _clicks = 0;
                _helpDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                _grid = ZoneBuilder.NextGrid(_grid);
                _helpDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                _gridYaw += Input.GetKey(KeyCode.LeftShift) ? -GridStep : GridStep;
                _gridYaw = ((_gridYaw % 360f) + 360f) % 360f;
                _helpDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.Backspace) && _clicks > 0)
            {
                _clicks--;
                _helpDirty = true;
            }
            if (Input.GetKeyDown(KeyCode.Delete)) RemoveTarget();
            if (Input.GetMouseButtonDown(0)) Click();
        }

        private void Click()
        {
            if (_clicks < 2 && !_hit)
            {
                Ctx.Notice.Show("Aim at the ground - nothing under the crosshair.", 2f);
                return;
            }
            if (_clicks == 0) { _a = _point; _clicks = 1; _helpDirty = true; return; }
            if (_clicks == 1) { _b = _point; _clicks = 2; _helpDirty = true; return; }

            float top;
            if (!TopNow(out top))
            {
                Ctx.Notice.Show("Look at the box from the side to set its height.", 2f);
                return;
            }
            Trigger box = ZoneBuilder.Finish(_a, _b, top, _gridYaw);
            string what = Place(box);
            _clicks = 0;
            _helpDirty = true;
            Ctx.Log.LogInfo("Zone editor: " + what + " of '" + _segment.Name + "' is now " + box.Describe() + ".");
            Ctx.Notice.Show(Capital(what) + " placed.", 2f);
        }

        private bool TopNow(out float top)
        {
            Transform t = _cam.Camera.transform;
            Trigger flat = ZoneBuilder.Footprint(_a, _b, _gridYaw);
            Vector3 middle = new Vector3(flat.Position.x, Mathf.Min(_a.y, _b.y), flat.Position.z);
            if (!ZoneBuilder.HeightFromRay(t.position, t.forward, middle, out top)) return false;
            top = ZoneBuilder.SnapHeight(top, ZoneBuilder.GridSizes[_grid]);
            return true;
        }

        /// The box into the picked slot; what it was ("the start", ...).
        private string Place(Trigger box)
        {
            int n = _segment.Checkpoints.Count;
            string what;
            if (_target == -1) { _segment.Start = box; what = "the start"; }
            else if (_target == n + 1) { _segment.End = box; what = "the end"; }
            else if (_target == n)
            {
                _segment.Checkpoints.Add(box);
                what = "checkpoint " + (n + 1);
                _target = n;   // stays on the one just made; Tab goes on to a new one
            }
            else { _segment.Checkpoints[_target] = box; what = "checkpoint " + (_target + 1); }
            _practice.EditedByZoneEditor(_segment);
            return what;
        }

        private void RemoveTarget()
        {
            int n = _segment.Checkpoints.Count;
            if (_target < 0 || _target >= n)
            {
                Ctx.Notice.Show("Only a checkpoint can be removed - a timed segment keeps its start and end.", 3f);
                return;
            }
            _segment.RemoveCheckpoint(_target);
            _practice.EditedByZoneEditor(_segment);
            Ctx.Log.LogInfo("Zone editor: removed checkpoint " + (_target + 1) + " of '" + _segment.Name + "'.");
            Ctx.Notice.Show("Checkpoint " + (_target + 1) + " removed.", 2f);
            if (_target > 0) _target--;
            _clicks = 0;
            _helpDirty = true;
        }

        private void Preview()
        {
            _draw.Show = true;
            _draw.HasPoint = _hit && _clicks < 2;
            _draw.Point = _point;
            _draw.Grid = ZoneBuilder.GridSizes[_grid];
            _draw.GridYaw = _gridYaw;
            _draw.Kind = _target == -1 ? 0 : _target == _segment.Checkpoints.Count + 1 ? 2 : 1;
            _draw.HasBox = false;
            if (_clicks == 1 && _hit) SetBox(ZoneBuilder.Footprint(_a, _point, _gridYaw));
            else if (_clicks == 2)
            {
                float top;
                SetBox(TopNow(out top) ? ZoneBuilder.Finish(_a, _b, top, _gridYaw) : ZoneBuilder.Footprint(_a, _b, _gridYaw));
            }
        }

        private void SetBox(Trigger t)
        {
            _draw.HasBox = true;
            _draw.BoxCentre = t.Position;
            _draw.BoxExtents = t.Extents;
            _draw.BoxYaw = t.Yaw;
        }

        // --- on screen --------------------------------------------------------

        public override void DrawScreenAlways()
        {
            if (!_active || Host.AnyPanelOpen()) return;
            if (_helpDirty) { _help.text = HelpText(); _helpDirty = false; }

            float w = Mathf.Min(760f, Screen.width - 40f);
            float x = (Screen.width - w) * 0.5f;
            float h = UiText.Height(w - 16f, _help, UiText.Plain) + 12f;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            WinPx.DrawTexture(new Rect(x, 14f, w, h), Texture2D.whiteTexture);
            GUI.color = old;
            UiText.Draw(x + 8f, 20f, w - 16f, _help, UiText.Plain);

            if (Event.current.type == EventType.Repaint) Crosshair();
        }

        private string HelpText()
        {
            string step = _clicks == 0 ? "click the first corner" : _clicks == 1 ? "click the opposite corner" : "look up or down, click to set the height";
            float g = ZoneBuilder.GridSizes[_grid];
            return "Zone editor - " + _segment.Name + ": " + TargetName() + ", " + step + ".\n" +
                   "Tab / Shift+Tab: start, checkpoints, end  |  G grid: " + (g <= 0f ? "off" : g.ToString("0.#") + " m") +
                   "  |  R / Shift+R turn grid (" + _gridYaw.ToString("0") + " deg)  |  Backspace undo a click  |  " +
                   "Delete remove checkpoint  |  WASD QE fly, Shift fast  |  Enter or Esc: done";
        }

        private string TargetName()
        {
            int n = _segment.Checkpoints.Count;
            if (_target == -1) return "the start";
            if (_target == n + 1) return "the end";
            if (_target == n) return "a new checkpoint (" + (n + 1) + ")";
            return "checkpoint " + (_target + 1);
        }

        private static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        private static void Crosshair()
        {
            float cx = Mathf.Round(Screen.width * 0.5f), cy = Mathf.Round(Screen.height * 0.5f);
            Texture2D t = Texture2D.whiteTexture;
            WinPx.DrawTexture(new Rect(cx - 8f, cy - 1f, 6f, 2f), t);
            WinPx.DrawTexture(new Rect(cx + 2f, cy - 1f, 6f, 2f), t);
            WinPx.DrawTexture(new Rect(cx - 1f, cy - 8f, 2f, 6f), t);
            WinPx.DrawTexture(new Rect(cx - 1f, cy + 2f, 2f, 6f), t);
        }
    }

    // ------------------------------------------------------------------
    // The editor's world lines: the box being drawn (in its target's
    // colour), a patch of the grid around the crosshair's point and a post
    // through it. The segment's own zones are Game/ZonePreview's.
    // ------------------------------------------------------------------
    public sealed class ZoneEditorDraw : MonoBehaviour
    {
        public bool Show, HasPoint, HasBox;
        public Vector3 Point, BoxCentre, BoxExtents;
        public float Grid, GridYaw, BoxYaw;
        public int Kind;   // 0 start, 1 checkpoint, 2 end

        private static readonly Color[] Colours =
        {
            new Color(0.38f, 0.78f, 0.45f, 0.95f), new Color(0.95f, 0.72f, 0.26f, 0.95f), new Color(0.88f, 0.34f, 0.34f, 0.95f),
        };
        private static readonly Color GridColour = new Color(1f, 1f, 1f, 0.35f);
        private Material _material;

        private void OnRenderObject()
        {
            try
            {
                if (!Show || !DrawTarget.ShouldDraw()) return;
                if (_material == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null) return;
                    _material = new Material(shader);
                    _material.hideFlags = HideFlags.HideAndDontSave;
                    _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _material.SetInt("_ZWrite", 0);
                }
                _material.SetPass(0);
                GL.PushMatrix();
                GL.Begin(GL.LINES);

                if (HasPoint)
                {
                    GL.Color(GridColour);
                    float step = Grid > 0f ? Grid : 1f;
                    const int half = 4;
                    Quaternion r = Quaternion.Euler(0f, GridYaw, 0f);
                    for (int i = -half; i <= half; i++)
                    {
                        Line(Point + r * new Vector3(i * step, 0.05f, -half * step), Point + r * new Vector3(i * step, 0.05f, half * step));
                        Line(Point + r * new Vector3(-half * step, 0.05f, i * step), Point + r * new Vector3(half * step, 0.05f, i * step));
                    }
                    GL.Color(Colours[Mathf.Clamp(Kind, 0, 2)]);
                    Line(Point, Point + Vector3.up * 2f);
                }

                if (HasBox)
                {
                    GL.Color(Colours[Mathf.Clamp(Kind, 0, 2)]);
                    WireBox(BoxCentre, BoxExtents, BoxYaw);
                }

                GL.End();
                GL.PopMatrix();
            }
            catch (System.Exception ex) { Lifecycle.Fail("ZoneEditorDraw.OnRenderObject", ex); }
        }

        private static void Line(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }

        // Reused every frame: nothing allocated while drawing (src/CLAUDE.md).
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly int[] Edges = { 0, 1, 1, 3, 3, 2, 2, 0, 4, 5, 5, 7, 7, 6, 6, 4, 0, 4, 1, 5, 3, 7, 2, 6 };

        private static void WireBox(Vector3 c, Vector3 e, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 8; i++)
                Corners[i] = c + r * new Vector3((i & 4) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 1) != 0 ? e.z : -e.z);
            for (int i = 0; i < Edges.Length; i += 2) Line(Corners[Edges[i]], Corners[Edges[i + 1]]);
        }
    }
}
