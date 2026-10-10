using System;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Paint (T-0219; author, 2026-10-10): hold a key and paint where the
    // crosshair hits, like KSF paint - for lineups and routes while
    // practising. Hold another to erase near the crosshair, press a third
    // to undo the last stroke, Clear all in Settings -> Paint.
    //
    // Practice mode (F9) only, and never in run mode ("too much if it's
    // allowed in runs"); nothing while a window or a game menu has the
    // mouse. Paint belongs to the selected spot and is saved with it
    // (config/ForestOverlay/paint/<spot id>.txt, its own folder - the spot
    // files and their site sync never see it); with no spot selected it
    // lasts until the game closes.
    //
    // Writes no game state, so not practice-only: it is simply not there
    // outside practice mode.
    // ------------------------------------------------------------------
    public sealed class PaintModule : OverlayModule
    {
        public override string Id { get { return "paint"; } }
        public override string DisplayName { get { return "Paint"; } }

        private const float Range = 300f;
        private const float EraseRadius = 0.3f;

        // The swatches (author: 8 presets). Drawn after the image effects,
        // so these are the colours seen.
        public static readonly Color32[] Palette = new Color32[]
        {
            new Color32(240, 60, 60, 235),    // red
            new Color32(255, 145, 30, 235),   // orange
            new Color32(255, 225, 40, 235),   // yellow
            new Color32(60, 220, 80, 235),    // green
            new Color32(50, 220, 240, 235),   // cyan
            new Color32(70, 120, 255, 235),   // blue
            new Color32(180, 90, 255, 235),   // purple
            new Color32(245, 245, 245, 235),  // white
        };

        /// The size steps, in metres (the - / + buttons).
        private static readonly float[] Sizes = { 0.02f, 0.04f, 0.06f, 0.08f, 0.1f, 0.15f, 0.2f, 0.3f, 0.5f };

        private ConfigEntry<int> _colourCfg;
        private ConfigEntry<float> _sizeCfg;
        private HotkeyMap.Binding _paintKey, _eraseKey, _undoKey;

        private GameObject _host;
        private PaintDraw _draw;
        private string _folder;

        private readonly PaintSet _session = new PaintSet();
        private PaintSet _spotSet;
        private string _spotId;
        private string _spotName = "";
        private int _savedVersion;

        private bool _painting, _erasing;
        private int _erased;
        private float _offNoticeAt = -100f;
        private bool _fullSaid;

        private PracticeModule _practice;
        private PracticeRunModule _runs;

        public PaintSet Current { get { return _spotSet ?? _session; } }

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _colourCfg = ctx.Config.Bind("Paint", "Colour", 0, "The paint colour: 0 red, 1 orange, 2 yellow, 3 green, 4 cyan, 5 blue, 6 purple, 7 white.");
            _sizeCfg = ctx.Config.Bind("Paint", "Size", 0.1f, "The paint's width in metres (0.02 - 0.5).");
            _folder = Path.Combine(ctx.ConfigDirectory, "paint");

            _host = new GameObject("ForestOverlay_Paint");
            _host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _draw = _host.AddComponent<PaintDraw>();
            _draw.Palette = Palette;
            _draw.Source = _session;
        }

        public override void RegisterHotkeys(HotkeyMap map)
        {
            // Held keys: read in Tick, so their action does nothing.
            map.Add("paint.paint", KeyCode.Mouse3, "Paint (hold)", Nothing);
            map.Add("paint.erase", KeyCode.Mouse4, "Erase paint (hold)", Nothing);
            map.Add("paint.undo", KeyCode.Z, "Undo the last paint stroke", UndoKey);
            for (int i = 0; i < map.Bindings.Count; i++)
            {
                HotkeyMap.Binding b = map.Bindings[i];
                if (b.Id == "paint.paint") _paintKey = b;
                else if (b.Id == "paint.erase") _eraseKey = b;
                else if (b.Id == "paint.undo") _undoKey = b;
            }
        }

        private static void Nothing() { }

        // --- state ------------------------------------------------------------

        /// Practice mode on, no run mode, in the world.
        private bool Active
        {
            get
            {
                if (_runs == null) _runs = Host.Find<PracticeRunModule>();
                if (_runs == null || !_runs.Enabled) return false;
                if (Ctx.Run != null && Ctx.Run.Active) return false;
                return Ctx.Player.Found && !PlayerRef.AtTitleScreen;
            }
        }

        /// The mouse is the game's (no window, no game menu).
        private bool Aiming { get { return !Host.AnyPanelOpen() && !Cursor.visible; } }

        private static bool Held(HotkeyMap.Binding b)
        {
            return b != null && b.Key != KeyCode.None && Input.GetKey(b.Key);
        }

        private static bool Pressed(HotkeyMap.Binding b)
        {
            return b != null && b.Key != KeyCode.None && Input.GetKeyDown(b.Key);
        }

        private int Colour { get { return Mathf.Clamp(_colourCfg.Value, 0, Palette.Length - 1); } }
        private float Size { get { return PaintSet.ClampSize(_sizeCfg.Value); } }

        public override void Tick()
        {
            FollowSpot();

            bool active = Active;
            _draw.Visible = active;

            bool paintHeld = Held(_paintKey);
            bool eraseHeld = Held(_eraseKey);

            if (!active || !Aiming)
            {
                if (_painting) EndPaint();
                if (_erasing) EndErase();
                // Pressed in the world with practice mode off: say why nothing happens.
                if (!active && Aiming && Ctx.Player.Found && !PlayerRef.AtTitleScreen && (Pressed(_paintKey) || Pressed(_eraseKey)))
                    SayOff();
                return;
            }

            if (paintHeld && !eraseHeld) PaintFrame();
            else if (_painting) EndPaint();

            if (eraseHeld && !paintHeld) EraseFrame();
            else if (_erasing) EndErase();
        }

        private void SayOff()
        {
            // At most every 30 s: a side button is often push-to-talk.
            if (Ctx.Run != null && Ctx.Run.Active) return;
            if (Time.unscaledTime - _offNoticeAt < 30f) return;
            _offNoticeAt = Time.unscaledTime;
            Ctx.Notice.Show("Paint works in practice mode (F9).", 4f);
        }

        private void PaintFrame()
        {
            PaintSet set = Current;
            if (!_painting)
            {
                _painting = true;
                _fullSaid = false;
                set.BeginStroke(Colour, Size);
            }
            if (set.Full)
            {
                if (!_fullSaid)
                {
                    _fullSaid = true;
                    Ctx.Notice.Show("Paint is full (" + PaintSet.MaxDots + " dots). Erase or clear some first.", 5f);
                }
                return;
            }
            Vector3 p, n;
            if (PaintDraw.Aim(Ctx.Player.Transform, Range, out p, out n)) set.AddPoint(p, n);
        }

        private void EndPaint()
        {
            _painting = false;
            PaintSet set = Current;
            int dots = set.EndStroke();
            if (dots == 0) return;
            Ctx.Log.LogInfo("Paint: stroke of " + dots + " dot(s), colour " + Colour + ", size " + SizeText(Size) + " - " + WhereText() + " (" + set.Count + " in all)");
            Save();
        }

        private void EraseFrame()
        {
            _erasing = true;
            Vector3 p, n;
            if (PaintDraw.Aim(Ctx.Player.Transform, Range, out p, out n))
                _erased += Current.EraseNear(p, Mathf.Max(EraseRadius, Size));
        }

        private void EndErase()
        {
            _erasing = false;
            if (_erased == 0) return;
            Ctx.Log.LogInfo("Paint: erased " + _erased + " dot(s) - " + WhereText() + " (" + Current.Count + " left)");
            _erased = 0;
            Save();
        }

        private void UndoKey()
        {
            if (!Active || !Aiming) return;
            Undo(false);
        }

        /// Returns what happened, for the Settings button's message.
        private string Undo(bool fromButton)
        {
            if (_painting) EndPaint();
            int n = Current.Undo();
            if (n == 0)
            {
                if (!fromButton) Ctx.Notice.Show("Nothing to undo.", 3f);
                return "Nothing to undo.";
            }
            Ctx.Log.LogInfo("Paint: undid a stroke of " + n + " dot(s) - " + WhereText() + " (" + Current.Count + " left)");
            Save();
            return "Took back a stroke of " + n + " dot(s).";
        }

        private string Clear()
        {
            if (_painting) EndPaint();
            int n = Current.Clear();
            if (n == 0) return "No paint to clear.";
            Ctx.Log.LogInfo("Paint: cleared " + n + " dot(s) - " + WhereText());
            Save();
            return "Cleared " + n + " dot(s).";
        }

        private string WhereText()
        {
            return _spotId != null ? "spot '" + _spotId + "'" : "no spot (this session)";
        }

        // --- the selected spot's paint ------------------------------------------

        private void FollowSpot()
        {
            if (_practice == null) _practice = Host.Find<PracticeModule>();
            Segment s = _practice != null ? _practice.SelectedSegment : null;
            string id = s != null && s.Id.Length > 0 ? s.Id : null;
            if (id == _spotId)
            {
                if (s != null) _spotName = s.Name;
                return;
            }

            if (_painting) EndPaint();
            if (_erasing) EndErase();

            _spotId = id;
            _spotName = s != null ? s.Name : "";
            _spotSet = id != null ? Load(id) : null;
            _savedVersion = _spotSet != null ? _spotSet.Version : 0;
            _draw.Source = Current;
        }

        private string PathFor(string id)
        {
            return Path.Combine(_folder, PaintSet.FileNameFor(id));
        }

        private PaintSet Load(string id)
        {
            string path = PathFor(id);
            try
            {
                string recovered = SafeFile.Recover(path);
                if (recovered.Length > 0) Ctx.Log.LogInfo("Paint: " + recovered + " (" + path + ")");
                if (!File.Exists(path)) return new PaintSet();
                int bad;
                PaintSet set = PaintSet.Parse(File.ReadAllText(path, Encoding.UTF8), out bad);
                Ctx.Log.LogInfo("Paint: loaded " + set.Count + " dot(s) for spot '" + id + "'" + (bad > 0 ? ", " + bad + " line(s) unreadable" : ""));
                return set;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Paint: could not read " + path + ": " + ex.Message);
                return new PaintSet();
            }
        }

        private void Save()
        {
            if (_spotSet == null || _spotId == null) return;   // session paint: never written
            if (_spotSet.Version == _savedVersion) return;
            string path = PathFor(_spotId);
            try
            {
                if (_spotSet.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                else
                {
                    if (!Directory.Exists(_folder)) Directory.CreateDirectory(_folder);
                    SafeFile.WriteAllText(path, _spotSet.ToText(), new UTF8Encoding(false));
                }
                _savedVersion = _spotSet.Version;
            }
            catch (Exception ex)
            {
                Ctx.Log.LogWarning("Paint: could not save " + path + ": " + ex.Message);
                Ctx.Notice.Show("Paint could not be saved: " + ex.Message, 6f);
            }
        }

        public override void Shutdown()
        {
            if (_painting) EndPaint();
            if (_erasing) EndErase();
            if (_host != null) UnityEngine.Object.Destroy(_host);
        }

        // --- Settings -> Paint ----------------------------------------------------

        private readonly GUIContent _intro = new GUIContent("");
        private readonly GUIContent _where = new GUIContent("");
        private readonly GUIContent _sizeLabel = new GUIContent("");
        private readonly GUIContent _message = new GUIContent("");
        private KeyCode _introPaint = (KeyCode)(-1), _introErase, _introUndo;
        private string _whereId = "\u0001";
        private int _whereCount = -1;
        private float _sizeShown = -1f;
        private float _clearArmedAt = -100f;
        private int _messageAt;                    // 1 = under Undo / Clear
        private static readonly GUIContent SavedNote = new GUIContent(
            "Paint is saved with the selected spot and comes back with it. With no spot selected it stays until the game closes.");
        private static readonly GUIContent ColourLabel = new GUIContent("Colour");
        private static readonly GUIContent UndoText = new GUIContent("Undo last stroke");
        private static readonly GUIContent ClearText = new GUIContent("Clear all");
        private static readonly GUIContent ClearConfirmText = new GUIContent("Click again to clear");
        private GUIStyle _swatchStyle;

        private void RefreshTexts()
        {
            KeyCode p = _paintKey != null ? _paintKey.Key : KeyCode.None;
            KeyCode e = _eraseKey != null ? _eraseKey.Key : KeyCode.None;
            KeyCode u = _undoKey != null ? _undoKey.Key : KeyCode.None;
            if (p != _introPaint || e != _introErase || u != _introUndo)
            {
                _introPaint = p; _introErase = e; _introUndo = u;
                _intro.text = "Paint on the world while you practise: hold " + KeyText(p) + " to paint where the crosshair points, hold " +
                              KeyText(e) + " to rub paint out, press " + KeyText(u) + " to take back the last stroke. " +
                              "Works in practice mode (F9) with this window closed. The keys can be changed under Keys.";
            }

            PaintSet set = Current;
            string id = _spotId ?? "";
            if (id != _whereId || set.Count != _whereCount)
            {
                _whereId = id;
                _whereCount = set.Count;
                _where.text = _spotId != null
                    ? "Selected spot: " + _spotName + " - " + set.Count + " dot(s)"
                    : "No spot selected - " + set.Count + " dot(s), kept until the game closes";
            }

            float size = Size;
            if (size != _sizeShown)
            {
                _sizeShown = size;
                _sizeLabel.text = "Size: " + SizeText(size);
            }
        }

        private static string KeyText(KeyCode k)
        {
            return k == KeyCode.None ? "(unbound)" : HotkeyMap.KeyName(k);
        }

        private static string SizeText(float metres)
        {
            return Mathf.RoundToInt(metres * 100f) + " cm";
        }

        /// The Paint view of the Settings tab (SettingsModule draws the switch).
        public void DrawSettings(Rect area)
        {
            RefreshTexts();
            if (_swatchStyle == null)
            {
                _swatchStyle = new GUIStyle(GUI.skin.button);
                _swatchStyle.padding = new RectOffset(3, 3, 3, 3);
            }

            float x = area.x + 4f, w = area.width - 8f;
            float y = area.y;
            y += UiText.Draw(x, y, w, _intro) + 6f;
            y += UiText.DrawDim(x, y, w, SavedNote) + 6f;
            y += UiText.Draw(x, y, w, _where) + 8f;

            // Colour: the swatches, the chosen one framed.
            GUI.Label(new Rect(x, y, w, 22f), ColourLabel);
            y += 22f;
            int chosen = Colour;
            float sw = 30f;
            float sx = x;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (sx + sw > x + w) { sx = x; y += sw + 4f; }
                Rect r = new Rect(sx, y, sw, sw);
                if (GUI.Toggle(r, i == chosen, GUIContent.none, _swatchStyle) && i != chosen)
                {
                    _colourCfg.Value = i;
                    chosen = i;
                }
                Color old = GUI.color;
                GUI.color = Palette[i];
                float inset = i == chosen ? 6f : 3f;
                GUI.DrawTexture(new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f), Texture2D.whiteTexture);
                GUI.color = old;
                sx += sw + 4f;
            }
            y += sw + 8f;

            // Size: steps, so a click writes the config once (no slider drag).
            GUI.Label(new Rect(x, y, 110f, 22f), _sizeLabel);
            if (GUI.Button(new Rect(x + 114f, y, 30f, 22f), "-")) StepSize(-1);
            if (GUI.Button(new Rect(x + 148f, y, 30f, 22f), "+")) StepSize(+1);
            y += 30f;

            if (GUI.Button(new Rect(x, y, 150f, 24f), UndoText))
            {
                _message.text = Undo(true);
                _messageAt = 1;
            }
            y += 28f;

            bool armed = Time.unscaledTime - _clearArmedAt < 4f;
            if (GUI.Button(new Rect(x, y, 150f, 24f), armed ? ClearConfirmText : ClearText))
            {
                if (armed)
                {
                    _clearArmedAt = -100f;
                    _message.text = Clear();
                }
                else
                {
                    _clearArmedAt = Time.unscaledTime;
                    _message.text = Current.Count > 0
                        ? "This removes all " + Current.Count + " dot(s) " + (_spotId != null ? "of this spot" : "of this session") + "."
                        : "No paint to clear.";
                }
                _messageAt = 1;
            }
            y += 28f;

            if (_messageAt == 1 && _message.text.Length > 0) UiText.Draw(x, y, w, _message);
        }

        private void StepSize(int dir)
        {
            float now = Size;
            int at = 0;
            for (int i = 0; i < Sizes.Length; i++)
                if (Mathf.Abs(Sizes[i] - now) < Mathf.Abs(Sizes[at] - now)) at = i;
            at = Mathf.Clamp(at + dir, 0, Sizes.Length - 1);
            if (Sizes[at] != _sizeCfg.Value) _sizeCfg.Value = Sizes[at];
        }
    }
}
