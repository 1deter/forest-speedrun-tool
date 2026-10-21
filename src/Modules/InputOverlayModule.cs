using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Input overlay (T-0302; author, 2026-10-11): the keys and mouse buttons
    // you press, drawn on the game screen, modelled on girlglock/input-overlay.
    // It follows the HUD's show / hide (F5 hides it), like the splits panel.
    //
    // - Fully modular: every tile (a key, the mouse box, the wheel) is placed
    //   on its own, shown or hidden, sized on its own (Data/InputLayout). The
    //   default is W A S D, Shift, Space, Ctrl, M1, M2 and the mouse box.
    // - Two sources (T-0361, @Inputs.Source): Game input (default) reads each
    //   key's shipped-default game action through Game/GameActions; Raw input
    //   is the physical keys (UnityEngine.Input.GetKey): what the fingers did,
    //   whatever the game's bindings. Unity gives digital keys, so there is
    //   no analogue fill - a press fills the tile in the accent colour with
    //   a short pop, as the reference does for a digital key.
    // - Nothing is drawn while a cursor shows (menus, inventory, the F2
    //   window) - except in Edit HUD, where the tiles can be placed and
    //   light up for trying.
    // - Set up where it is shown (Edit HUD > Panels), saved in the HUD
    //   profile (@Inputs.On, @Inputs.Layout).
    // - Sampled in Tick (one Input.GetKey per tile, no allocation), drawn on
    //   Repaint only from cached contents. Off = nothing sampled or drawn.
    //
    // Writes no game state: not practice-only, available in run mode.
    // ------------------------------------------------------------------
    public sealed class InputOverlayModule : OverlayModule
    {
        public override string Id { get { return "inputs"; } }
        public override string DisplayName { get { return "Input overlay"; } }

        private const float PopAmount = 0.1f, RiseTime = 0.1f, FallTime = 0.2f, MinLit = 0.05f;
        private const float TrailSeconds = 0.6f, MouseGain = 0.02f, TrailSpacing = 0.08f, CometSeconds = 0.4f;
        private const int CometMax = 128;   // 0.4 s of a point per frame, to 300 fps
        private const int TrailMax = 48;

        private sealed class TileState
        {
            public InputTile Tile;
            public KeyCode Code;
            public bool Valid;       // drawn and sampled: the key is known and, for a per-action tile, the game input shows it
            public bool KeyOk;       // the key name parsed
            public bool Skip;        // a per-action tile not shown now (raw input, actions unknown, or the key lost that action)
            public bool Special;     // the mouse box or the wheel
            public float Level;      // 0 idle .. 1 pressed (the animation)
            public float LitUntil;
            public bool Active;      // held this frame: the only-pressed mode draws only these (key and button tiles)
            public GUIContent Label, Name;
            public GUIContent RawLabel, RawName;   // the key's own words (raw input)
            // Game input (T-0361): the key's shipped-default actions in pick order.
            public int Count;                      // 0: none (dead when the actions are read) / raw
            public readonly int[] Acts = new int[InputActionTable.MaxPerKey], Signs = new int[InputActionTable.MaxPerKey];
            public readonly GUIContent[] ActLabels = new GUIContent[InputActionTable.MaxPerKey];
            public bool Dead;                      // game input, the key has no game action
            public int MenuAct = -1;               // the Esc action's index: its press shows the cursor (the pause menu) at once
            public readonly int[] Slots = new int[InputActionTable.MaxPerKey];   // ActionReaders' slot per action
            public GUIContent Idle;                // the functions' names, one a line, in pick order
            public bool UsesFsm;                   // an attack / block action: the player's control FSM tells which
            public bool WasHeld, WasLit;
            public int ReadAct = -1;               // the action a game script read in this press (-1 none yet)
            public readonly bool[] Cont = new bool[InputActionTable.MaxPerKey], FsmAct = new bool[InputActionTable.MaxPerKey], HeldNow = new bool[InputActionTable.MaxPerKey];
            public int PressFrame, Strength;
            public string Performed;               // the function performed in this press (null: none yet)
            public float LingerUntil;
            public GUIContent FitFor;              // the label the fit below was computed for
            public int FitSize;
            public float FitScale;
            public string SnapId;
            public Rect Rect;        // as last drawn (virtual pixels)
        }

        private ConfigEntry<bool> _on;
        private ConfigEntry<bool> _onlyPressed;
        private ConfigEntry<string> _source;
        private ConfigEntry<string> _mouseStyle;
        private const string StyleComet = "Comet", StyleBox = "Box";
        private const string SourceGame = "Game", SourceRaw = "Raw";
        private bool _appliedGame, _appliedReady, _applied;
        private ConfigEntry<string> _layoutCfg;
        private InputLayout _layout = new InputLayout();
        private readonly List<TileState> _states = new List<TileState>();

        // The mouse box: where the mouse has "gone" (wrapping at the edges)
        // and its fading trail.
        private float _mx, _my;
        private readonly float[] _trailX = new float[TrailMax], _trailY = new float[TrailMax], _trailT = new float[TrailMax];
        private int _trailHead, _trailCount;
        // The Comet (T-0385): the accumulated path, not wrapped; drawn relative to the head.
        private float _ux, _uy;
        private readonly float[] _cometX = new float[CometMax], _cometY = new float[CometMax], _cometT = new float[CometMax];
        private int _cometHead, _cometCount;
        private float _cometLastMove = -100f, _cometFit = 1f, _cometFitAt;
        private bool Comet { get { return _mouseStyle.Value != StyleBox; } }
        private bool _mouseMoved;   // this frame's raw mouse delta was non-zero (Only while pressed hides a still mouse box)
        private float _lastTx, _lastTy;
        private bool _axisBroken;
        private bool _axisTold;

        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        // Edit HUD state.
        private bool _capturing;
        private int _drag = -1;
        private int _resize = -1;   // a tile whose corner handle is being dragged
        private const float HandlePx = 12f;
        private float _resizeStartScale;
        private Vector2 _resizeStartMouse;
        private Vector2 _dragOffset;
        private int _removeAt = -1;
        private bool _resetArmed;
        private float _resetUntil;
        private readonly GUIContent _status = new GUIContent("");

        private GUIStyle _idle, _lit;

        public override void Initialise(ModuleContext ctx)
        {
            base.Initialise(ctx);
            _on = ctx.Config.Bind("Inputs", "On", false, "Show the input overlay: the keys and mouse buttons you press, on the game screen.");
            _onlyPressed = ctx.Config.Bind("Inputs", "OnlyPressed", false, "Show a tile only while its key is held (exact, no fade). Edit HUD still shows every tile.");
            _source = ctx.Config.Bind("Inputs", "Source", SourceGame,
                "What the tiles read: Game (the game's actions: Jump, Crouch, a replay's inputs) or Raw (the keys you press).");
            _mouseStyle = ctx.Config.Bind("Inputs", "MouseStyle", StyleComet,
                "How the mouse tile shows movement: Comet (a fading tail, no frame) or Box (a framed box with a trail that wraps at the edges).");
            _layoutCfg = ctx.Config.Bind("Inputs", "Layout", DefaultLayout().Format(),
                "The input overlay's tiles: key, x, y, size, shown (1/0) and, for a key's further game actions, the action name, separated by ';'; a trailing !split says those tiles were added. Set up in Edit HUD.");
            HudWidgets widgets = Host.Hud.Widgets;
            widgets.RegisterProfileEntry(_on, null);
            widgets.RegisterProfileEntry(_onlyPressed, null);
            widgets.RegisterProfileEntry(_source, null);
            widgets.RegisterProfileEntry(_mouseStyle, null);
            widgets.RegisterProfileEntry(_layoutCfg, delegate (object v)
            {
                _layoutCfg.BoxedValue = v;
                Reload();
            }, ProfileUnit.InputTiles);
            Reload();
        }

        private InputLayout DefaultLayout()
        {
            UiScale ui = Host.Hud.Settings.Scale();
            return ui.HasReference ? InputLayout.Default(ui.RefW, ui.RefH) : InputLayout.Default();
        }

        private void Reload()
        {
            _layout = InputLayout.Parse(_layoutCfg.Value);
            _states.Clear();
            for (int i = 0; i < _layout.Tiles.Count; i++) _states.Add(MakeState(_layout.Tiles[i], i));
            _drag = -1;
            _applied = false;
        }

        private TileState MakeState(InputTile t, int index)
        {
            TileState s = new TileState();
            s.Tile = t;
            s.SnapId = "input" + index + t.Key + t.Act;
            string label = InputLayout.LabelFor(t.Key);
            s.RawLabel = new GUIContent(label);
            s.RawName = new GUIContent(t.Key == InputLayout.MouseKey ? "Mouse box" : t.Key == InputLayout.WheelKey ? "Mouse wheel" : label);
            s.Label = s.RawLabel;
            s.Name = s.RawName;
            if (t.Key == InputLayout.MouseKey || t.Key == InputLayout.WheelKey) { s.Valid = true; s.KeyOk = true; s.Special = true; return s; }
            try
            {
                s.Code = (KeyCode)Enum.Parse(typeof(KeyCode), t.Key);
                s.KeyOk = true;
                s.Valid = true;
            }
            catch (Exception) { s.Valid = false; }   // a key a newer version knew: kept in the file, not drawn
            SetUpSource(s);
            return s;
        }

        private bool GameInput { get { return _source.Value != SourceRaw; } }

        // Game input: what a key stands for and how its tile reads. Rebuilt on a
        // source change, when the game's actions become readable, and per new tile.
        private void SetUpSource(TileState s)
        {
            s.Count = 0;
            s.Dead = false;
            s.MenuAct = -1;
            s.Label = s.RawLabel;
            s.Name = s.RawName;
            // A per-action tile (T-0409) exists only in game input, with the actions known.
            bool actTile = s.Tile.Act.Length > 0;
            s.Skip = actTile && (!GameInput || !GameActions.Ready);
            s.Valid = s.KeyOk && !s.Skip;
            if (s.Special || !s.Valid || !GameInput || !GameActions.Ready) return;
            InputActionTable table = GameActions.Table;
            int total = table.Resolve(s.Tile.Key, _pickActs, _pickSigns);
            int pick = total > 0 ? 0 : -1;   // a key's own tile stands for its first action
            if (actTile)
            {
                pick = -1;
                for (int k = 0; k < total; k++)
                    if (string.Equals(table.NameOf(_pickActs[k]), s.Tile.Act, StringComparison.Ordinal)) { pick = k; break; }
                if (pick < 0) { s.Skip = true; s.Valid = false; return; }   // the key no longer has this action: kept in the file, not drawn
            }
            if (pick < 0)
            {
                s.Dead = true;   // drawn dimmed; its row says why
                s.Name = new GUIContent(s.RawName.text + " - no game action on this key");
                return;
            }
            s.Count = 1;
            s.Acts[0] = _pickActs[pick];
            s.Signs[0] = _pickSigns[pick];
            string action = table.NameOf(s.Acts[0]);
            string word = InputActionTable.Label(action, s.Signs[0]);
            s.ActLabels[0] = LabelContent(word);
            s.Cont[0] = ActionFunctions.IsContinuous(action);
            s.FsmAct[0] = ActionFunctions.UsesControlState(action);
            s.UsesFsm = s.FsmAct[0];
            if (action == "Esc") s.MenuAct = 0;
            int all = table.Total(s.Tile.Key);
            if (!actTile && all > InputActionTable.MaxPerKey && _overflowTold.Add(s.Tile.Key))
                Ctx.Log.LogInfo("Inputs: key " + s.Tile.Key + " has " + all + " game actions, tiles are made for the first " + InputActionTable.MaxPerKey + ".");
            // Idle: the action's name until it is performed.
            s.Idle = new GUIContent(ActionFunctions.Idle(new string[] { word }, 1));
            s.Label = s.Idle;
            s.Performed = null;
            s.WasHeld = false;
            s.WasLit = false;
            s.ReadAct = -1;
            s.LingerUntil = 0f;
            s.Name = new GUIContent(s.RawName.text + " - " + word);
        }

        private readonly int[] _pickActs = new int[InputActionTable.MaxPerKey], _pickSigns = new int[InputActionTable.MaxPerKey];

        // One GUIContent per word, made when first seen (a press), never per frame.
        private readonly Dictionary<string, GUIContent> _words = new Dictionary<string, GUIContent>();

        private GUIContent LabelContent(string word)
        {
            GUIContent c;
            if (!_words.TryGetValue(word, out c)) { c = new GUIContent(word); _words[word] = c; }
            return c;
        }

        private bool _watching;
        private readonly HashSet<string> _overflowTold = new HashSet<string>();

        // Which actions the shown tiles stand for: the readers watch exactly those, and only while
        // the overlay is on in game input.
        private void ApplySource()
        {
            // T-0409: once the game's actions are known, a key with several actions gets a tile
            // per action beside its own (a layout never split; Reset profile starts again).
            if (GameInput && GameActions.Ready && !_layout.Split)
            {
                UiScale ui = Host.Hud.Settings.Scale();
                int n = _layout.SplitAll(GameActions.Table, ui.HasReference ? ui.RefW : 1920, ui.HasReference ? ui.RefH : 1080);
                Commit();
                Reload();
                Ctx.Log.LogInfo("Inputs: " + n + " per-action tiles added beside keys with several game actions.");
            }
            _applied = true;
            _appliedGame = GameInput;
            _appliedReady = GameActions.Ready;
            for (int i = 0; i < _states.Count; i++) SetUpSource(_states[i]);
            List<string> names = new List<string>();
            if (_on.Value && GameInput && GameActions.Ready)
                for (int i = 0; i < _states.Count; i++)
                    for (int k = 0; k < _states[i].Count; k++)
                    {
                        string n = GameActions.Table.NameOf(_states[i].Acts[k]);
                        if (!names.Contains(n)) names.Add(n);
                    }
            ActionReaders.Watch(Ctx.Log, OverlayPlugin.PluginGuid, names.ToArray());
            _watching = names.Count > 0;
            for (int i = 0; i < _states.Count; i++)
                for (int k = 0; k < _states[i].Count; k++)
                    _states[i].Slots[k] = ActionReaders.SlotOf(GameActions.Table.NameOf(_states[i].Acts[k]));
        }

        private void StopWatching()
        {
            if (!_watching) return;
            _watching = false;
            _applied = false;
            ActionReaders.Watch(Ctx.Log, OverlayPlugin.PluginGuid, null);
        }

        // A game-input tile is lit and labelled only for what the game DID: a continuous action
        // (movement, jump, run, crouch, attack: the controller reads it every frame) while held,
        // any other action only once a game script read it in this press (Take with nothing in
        // focus, Batch in the open world: held, but the game did nothing). The label is the
        // function performed: a reader that named it wins; else the control FSM for attack /
        // block; else the held action's own word. Rules in Data/ActionFunctions.Pick.
        private const float Linger = 0.75f;

        private void Perform(TileState s, int hit)
        {
            string reader = null;
            int bestFrame = -2;
            for (int k = 0; k < s.Count; k++)
            {
                int f;
                string w = ActionReaders.LabelOf(s.Slots[k], out f);
                if (w != null && f >= s.PressFrame - 1 && f > bestFrame) { reader = w; bestFrame = f; }
            }
            string fsm = null;
            int rank = 0;
            if (s.UsesFsm && s.Strength < 100 && reader == null) fsm = ActionFunctions.FsmLabel(ActionReaders.ControlState(), out rank);
            string fallback = s.FsmAct[hit] ? null : InputActionTable.Label(GameActions.Table.NameOf(s.Acts[hit]), s.Signs[hit]);
            int strength;
            s.Performed = ActionFunctions.Pick(reader, fsm, rank, s.Performed, s.Strength, fallback, out strength);
            s.Strength = strength;
        }

        private bool Editing { get { return Host != null && Host.Hud.Widgets != null && Host.Hud.Widgets.Editing; } }

        // --- sampling (Tick) -------------------------------------------------------------

        // The old default sat on the game's bottom-left messages: a layout
        // never moved goes to the new one (author, 2026-10-15, T-0325). On
        // the first Tick, after the active HUD profile was put in (it holds
        // the layout too), so the profile is written with the new one.
        private bool _oldDefaultChecked;

        private void MoveOldDefault()
        {
            _oldDefaultChecked = true;
            UiScale ui = Host.Hud.Settings.Scale();
            if (!InputLayout.IsOldDefault(_layoutCfg.Value, ui.HasReference ? ui.RefW : 1920, ui.HasReference ? ui.RefH : 1080)) return;
            _layoutCfg.Value = DefaultLayout().Format();
            Reload();
            Ctx.Log.LogInfo("Inputs: the old default layout moved to bottom centre (clear of the game's messages).");
        }

        public override void Tick()
        {
            if (!_oldDefaultChecked) MoveOldDefault();
            if (_on == null || !_on.Value) { _capturing = false; StopWatching(); return; }
            bool editing = Editing;
            if (!editing) _capturing = false;
            if (_capturing) Host.Hotkeys.HoldCapture();
            if (GameInput) GameActions.Ensure(Ctx.Log);
            if (!_applied || GameInput != _appliedGame || (GameInput && GameActions.Ready != _appliedReady)) ApplySource();
            // The open window turns the game's maps off: Edit HUD lights from the keys.
            bool useActions = GameInput && GameActions.Ready && !editing;
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            // A replay with recorded keys (T-0344) plays through GameActions: the
            // tiles show it although the replay bar frees the cursor.
            bool replay = useActions && GameActions.ReplayActive;
            bool live = editing || !Cursor.visible || replay;
            if (_capturing) CaptureKey();

            _mouseMoved = false;
            float scroll = live && !replay ? Input.mouseScrollDelta.y : 0f;
            for (int i = 0; i < _states.Count; i++)
            {
                TileState s = _states[i];
                if (!s.Valid || !s.Tile.Shown) { s.Level = 0f; s.Active = false; continue; }
                bool down = false;
                // The Esc press opens the pause menu, whose cursor shows that very frame: its tile
                // keeps sampling, or the press that opens the menu would never light (T-0391).
                if (live || s.MenuAct >= 0)
                {
                    if (!s.Special)
                    {
                        if (!useActions)
                        {
                            // Edit HUD (the window turns the game's maps off) lights from the key, except
                            // in game input a key with no game action, which never lights.
                            down = !s.Dead && Input.GetKey(s.Code);
                            if (s.Count > 0) s.Label = s.Idle;
                        }
                        else if (s.Count > 0)
                        {
                            bool anyHeld = false;
                            for (int k = 0; k < s.Count; k++)
                            {
                                bool held = GameActions.Held(s.Acts[k], s.Signs[k]) || (k == s.MenuAct && Input.GetKey(s.Code));
                                s.HeldNow[k] = held;
                                ActionReaders.Report(s.Slots[k], held);
                                anyHeld |= held;
                            }
                            if (anyHeld && !s.WasHeld) { s.PressFrame = Time.frameCount; s.Strength = 0; s.Performed = null; s.ReadAct = -1; }
                            s.WasHeld = anyHeld;
                            int hit = -1;
                            for (int k = 0; k < s.Count; k++)
                            {
                                if (!s.HeldNow[k]) continue;
                                if (s.Cont[k]) { if (hit < 0) hit = k; }
                                else if (s.ReadAct < 0 && ActionReaders.ReadFrame(s.Slots[k]) >= s.PressFrame - 1) s.ReadAct = k;
                            }
                            if (hit < 0 && s.ReadAct >= 0 && s.HeldNow[s.ReadAct]) hit = s.ReadAct;
                            down = hit >= 0;
                            if (down) Perform(s, hit);
                            else if (s.WasLit) s.LingerUntil = now + Linger;
                            s.WasLit = down;
                            s.Label = s.Performed != null && (down || now < s.LingerUntil) ? LabelContent(s.Performed) : s.Idle;
                        }
                    }
                    else if (s.Tile.Key == InputLayout.WheelKey) down = scroll != 0f;
                }
                s.Active = down;
                if (_onlyPressed.Value && !editing && (!s.Special || s.Tile.Key == InputLayout.WheelKey)) { s.LitUntil = 0f; s.Level = down ? 1f : 0f; continue; }   // frame-true: no latch, no animation
                if (down) s.LitUntil = now + MinLit;
                bool lit = down || now < s.LitUntil;
                if (lit) s.Level = Mathf.Min(1f, s.Level + dt / RiseTime);
                else s.Level = Mathf.Max(0f, s.Level - dt / FallTime);
            }
            if (HasShownMouseBox()) SampleMouse(live, now, useActions);
            if (_onlyPressed.Value && !editing && !_mouseMoved) _trailCount = 0;   // hidden while still: the old path never comes back
        }

        private bool HasShownMouseBox()
        {
            for (int i = 0; i < _states.Count; i++)
                if (_states[i].Special && _states[i].Tile.Key == InputLayout.MouseKey && _states[i].Tile.Shown) return true;
            return false;
        }

        private void SampleMouse(bool live, float now, bool useActions)
        {
            if (!live || _axisBroken) return;
            float dx, dy;
            try
            {
                dx = Input.GetAxisRaw("Mouse X");
                dy = Input.GetAxisRaw("Mouse Y");
                if (useActions)
                {
                    // The game's Mouse X / Y actions: the same numbers in play, and a replay's look shows.
                    float gx = GameActions.Axis("Mouse X"), gy = GameActions.Axis("Mouse Y");
                    if (!float.IsNaN(gx) && !float.IsNaN(gy)) { dx = gx; dy = gy; }
                }
            }
            catch (Exception ex)
            {
                _axisBroken = true;
                if (!_axisTold)
                {
                    _axisTold = true;
                    Ctx.Log.LogWarning("Inputs: the mouse box cannot read the mouse (" + ex.Message + ") - it stays still.");
                }
                return;
            }
            if (dx == 0f && dy == 0f) return;
            _mouseMoved = true;
            if (Comet) { SampleComet(dx, dy, now); return; }
            _mx = Wrap(_mx + dx * MouseGain);
            _my = Wrap(_my - dy * MouseGain);   // up on the mouse is up on the screen
            // A point only once the mouse has gone a little way from the last one,
            // so a slow move leaves a trail instead of dots stacked under the head.
            float tx = _mx - _lastTx, ty = _my - _lastTy;
            if (tx * tx + ty * ty < TrailSpacing * TrailSpacing) return;
            _lastTx = _mx;
            _lastTy = _my;
            _trailX[_trailHead] = _mx;
            _trailY[_trailHead] = _my;
            _trailT[_trailHead] = now;
            _trailHead = (_trailHead + 1) % TrailMax;
            if (_trailCount < TrailMax) _trailCount++;
        }

        private void SampleComet(float dx, float dy, float now)
        {
            if (CometPath.ShouldRestart(CometPath.Live(_cometT, _cometHead, _cometCount, CometMax, now, CometSeconds), now, _cometLastMove, CometSeconds))
            {
                _cometCount = 0;   // at rest, nothing alive: restart at the origin, so the numbers stay small
                _ux = 0f; _uy = 0f;
            }
            _cometLastMove = now;
            _ux += dx * MouseGain;
            _uy -= dy * MouseGain;   // up on the mouse is up on the screen
            if (!CometPath.IsNewPoint(dx * MouseGain, dy * MouseGain)) return;   // frame-true: a point on every moved frame, no spacing gate
            _cometX[_cometHead] = _ux;
            _cometY[_cometHead] = _uy;
            _cometT[_cometHead] = now;
            _cometHead = (_cometHead + 1) % CometMax;
            if (_cometCount < CometMax) _cometCount++;
        }

        private static float Wrap(float v)
        {
            while (v > 1f) v -= 2f;
            while (v < -1f) v += 2f;
            return v;
        }

        /// "Add input": the next key, mouse button or wheel step becomes a tile.
        private void CaptureKey()
        {
            if (Input.mouseScrollDelta.y != 0f)
            {
                _capturing = false;
                Host.Hotkeys.ReleaseCapture();
                AddTile(InputLayout.WheelKey);
                return;
            }
            if (!Input.anyKeyDown) return;
            for (int i = 0; i < AllKeys.Length; i++)
            {
                KeyCode k = AllKeys[i];
                if (!Input.GetKeyDown(k)) continue;
                if (k == KeyCode.Mouse0 || k == KeyCode.Mouse1)
                {
                    // the clicks that run this window: say so, keep waiting (T-0444)
                    string which = k == KeyCode.Mouse0 ? "M1" : "M2";
                    SetStatus(_layout.Find(k.ToString()) != null
                        ? which + " is already a tile. Press another input, or Esc to cancel."
                        : which + " cannot be added with Add input (the click is the press). Use + " + which + " beside it, or press another input. Esc cancels.");
                    continue;
                }
                _capturing = false;
                Host.Hotkeys.ReleaseCapture();
                if (k == KeyCode.Escape) { SetStatus("Cancelled."); return; }
                Host.Hotkeys.Swallow(k);   // the key only adds the tile; its own hotkey waits for the release
                string name = k.ToString();
                if (name.StartsWith("Joystick", StringComparison.Ordinal)) { SetStatus("Gamepad buttons are not shown."); return; }
                AddTile(name);
                return;
            }
        }

        // --- changes (Edit HUD) ----------------------------------------------------------------

        private void SetStatus(string text)
        {
            _status.text = text;
        }

        private void Commit()
        {
            _layoutCfg.Value = _layout.Format();
        }

        private void AddTile(string key)
        {
            UiScale ui = Host.Hud.Settings.Scale();
            InputTile have = _layout.Find(key);
            if (have != null)
            {
                // A key whose action tile was removed: Add puts the missing ones back beside its tile (T-0421).
                int back;
                string msg = _layout.AddAgain(key, GameInput && GameActions.Ready ? GameActions.Table : null, ui.RefW, ui.RefH, out back);
                if (back == 0) { SetStatus(msg); return; }
                Commit(); Reload();
                SetStatus(msg);
                Ctx.Log.LogInfo("Inputs: tile " + key + " got " + back + " action tile(s) back.");
                return;
            }
            // Bottom right, clear of the Edit HUD window, in the first free slot so new tiles do not stack
            // (author, 2026-10-15, T-0355).
            float nx, ny;
            _layout.FreeSlot(key, ui.RefW, ui.RefH, out nx, out ny);
            InputTile t = _layout.Add(key, nx, ny);
            if (t == null) { SetStatus("No room for another tile."); return; }
            _states.Add(MakeState(t, _states.Count));
            _applied = false;   // the readers watch the new tile's actions too
            int extra = 0;
            if (GameInput && GameActions.Ready) extra = _layout.SplitKey(t, GameActions.Table, ui.RefW, ui.RefH);
            if (extra > 0) { Commit(); Reload(); }
            else Commit();
            string added = "Added " + InputLayout.LabelFor(key) + " at the bottom right of the screen: drag it where you want it.";
            if (GameInput && GameActions.Ready && GameActions.Table.Total(key) == 0)
                added += " The game has no action on this key, so the tile stays dark under Game input; set Shows to Raw input to light it.";
            SetStatus(added);
            Ctx.Log.LogInfo("Inputs: tile " + key + " added.");
        }

        private void RemoveTile(int i)
        {
            string key = _states[i].Tile.Key, act = _states[i].Tile.Act;
            int others = act.Length == 0 ? _layout.CountActionTiles(key) : 0;
            _layout.Remove(key, act);
            Commit();
            Reload();
            SetStatus(InputLayout.RemovedText(key, others));
            Ctx.Log.LogInfo("Inputs: tile " + key + (act.Length > 0 ? " (" + act + ")" : "") + " removed.");
        }

        private void SetShown(TileState s, bool shown)
        {
            if (s.Tile.Shown == shown) return;
            s.Tile.Shown = shown;
            Commit();
            Ctx.Log.LogInfo("Inputs: tile " + InputLayout.TileName(s.Tile) + (shown ? " shown." : " hidden."));
        }

        private void ResetLayout()
        {
            _layoutCfg.Value = DefaultLayout().Format();
            Reload();
            SetStatus("Back to the default layout.");
            Ctx.Log.LogInfo("Inputs: layout reset to the default.");
        }

        // --- drawing -----------------------------------------------------------------------------

        private void EnsureStyles()
        {
            if (_idle != null) return;
            {
                GUIStyle idle = UiKit.Style(GUI.skin.label);
                idle.alignment = TextAnchor.MiddleCenter;
                idle.fontSize = LabelBase;
                idle.fontStyle = FontStyle.Bold;
                idle.padding = new RectOffset(0, 0, 0, 0);
                idle.wordWrap = false;
                idle.clipping = TextClipping.Clip;
                idle.normal.textColor = UiKit.TextColour;
                GUIStyle lit = UiKit.Style(idle);
                lit.normal.textColor = UiKit.OnAccent;
                _idle = idle;
                _lit = lit;
            }
        }

        private const int LabelBase = 15;   // the label's size at scale 1

        // The label is one ratio to its tile at every size (T-0363).
        private void SizeLabels(TileState s)
        {
            float scale = s.Tile.Scale;
            int size = Mathf.Max(6, Mathf.RoundToInt(LabelBase * scale));
            // A game-input label of several lines or long words shrinks to stay inside its tile.
            if (s.Count > 0)
            {
                if (!ReferenceEquals(s.FitFor, s.Label) || s.FitScale != scale)
                {
                    s.FitFor = s.Label;
                    s.FitScale = scale;
                    _idle.fontSize = size;
                    Vector2 sz = _idle.CalcSize(s.Label);
                    float f = Mathf.Min(1f, Mathf.Min((s.Rect.width - 4f * scale) / Mathf.Max(1f, sz.x), (s.Rect.height - 4f * scale) / Mathf.Max(1f, sz.y)));
                    s.FitSize = Mathf.Max(6, Mathf.FloorToInt(size * f));
                }
                size = s.FitSize;
            }
            if (_idle.fontSize != size) { _idle.fontSize = size; _lit.fontSize = size; }
        }

        private Rect TileRect(TileState s, UiScale ui)
        {
            InputTile t = s.Tile;
            float w = InputLayout.TileW(t.Key, t.Scale), h = InputLayout.TileH(t.Key, t.Scale);
            float x = Mathf.Clamp(ui.PosX(t.X), 0f, Mathf.Max(0f, ui.VW - w));
            float y = Mathf.Clamp(ui.PosY(t.Y), 0f, Mathf.Max(0f, ui.VH - h));
            return new Rect(x, y, w, h);
        }

        public override void DrawScreen()
        {
            if (_on == null || !_on.Value || _states.Count == 0) return;
            bool editing = Editing;
            if (!editing) FinishResize();   // Edit HUD closed mid-drag: keep the size reached
            // A menu's cursor hides the tiles, all but the Esc tile while it is lit: the press that
            // opened the pause menu shows over it (T-0391).
            bool menuOnly = !editing && Cursor.visible && !(GameInput && GameActions.Ready && GameActions.ReplayActive);
            if (menuOnly) { _drag = -1; if (!MenuLit()) return; }
            if (!editing) _drag = -1;

            Matrix4x4 outer = Host.Hud.Settings.BeginScaled();
            try
            {
                UiKit.Ensure();
                EnsureStyles();
                UiScale ui = Host.Hud.Settings.Scale();
                Event e = Event.current;
                for (int i = 0; i < _states.Count; i++)
                {
                    TileState s = _states[i];
                    if (!s.Valid) continue;
                    if (_drag != i) s.Rect = TileRect(s, ui);
                }
                if (editing && e != null) EditEvents(ui, e);
                if (e == null || e.type != EventType.Repaint) return;

                float now = Time.unscaledTime;
                Color before = GUI.color;
                for (int i = 0; i < _states.Count; i++)
                {
                    TileState s = _states[i];
                    if (!s.Valid) continue;
                    bool hidden = !s.Tile.Shown;
                    if (hidden && !editing) continue;
                    if (menuOnly && (s.MenuAct < 0 || s.Level <= 0f)) continue;
                    if (!editing && _onlyPressed.Value && (!s.Special || s.Tile.Key == InputLayout.WheelKey ? !s.Active : s.Tile.Key == InputLayout.MouseKey && !MouseLive(now))) continue;   // vanish: nothing on screen while not pressed
                    DrawTile(s, hidden, editing, now, before);
                    if (editing) Host.Hud.Widgets.ReportPiece(s.SnapId, s.Rect);
                }
                GUI.color = before;
            }
            finally { GUI.matrix = outer; }
        }

        private bool MenuLit()
        {
            for (int i = 0; i < _states.Count; i++)
                if (_states[i].MenuAct >= 0 && _states[i].Level > 0f && _states[i].Tile.Shown) return true;
            return false;
        }

        // Only while pressed: the Box shows on frames the mouse moved; the Comet too, and while its tail is alive.
        private bool MouseLive(float now)
        {
            if (_mouseMoved || !Comet) return _mouseMoved;
            return CometPath.Live(_cometT, _cometHead, _cometCount, CometMax, now, CometSeconds) > 0;
        }

        private void DrawTile(TileState s, bool hidden, bool editing, float now, Color before)
        {
            float a = hidden ? 0.35f : s.Dead ? 0.5f : 1f;   // a key with no game action is dimmed
            float level = s.Level;
            float pop = _onlyPressed.Value && !editing && (!s.Special || s.Tile.Key == InputLayout.WheelKey) ? 0f : PopAmount * level;
            Rect r = s.Rect;
            r = new Rect(r.x - r.width * pop * 0.5f, r.y - r.height * pop * 0.5f, r.width * (1f + pop), r.height * (1f + pop));

            GUI.color = new Color(before.r, before.g, before.b, before.a * a);
            float bw = Thick(BorderPx * s.Tile.Scale);
            bool frameless = s.Tile.Key == InputLayout.MouseKey && Comet;   // the Comet has no frame (Edit HUD outlines it)
            if (!frameless)
            {
                Flat(r, UiKit.WidgetEdge);
                Flat(Inset(r, bw), UiKit.WidgetFill);
                if (level > 0.01f)
                {
                    GUI.color = new Color(before.r, before.g, before.b, before.a * a * level);
                    Flat(r, UiKit.Accent);
                }
            }

            if (s.Tile.Key == InputLayout.MouseKey) DrawMouseBox(r, s.Tile.Scale, now, before, a);
            else
            {
                SizeLabels(s);
                GUI.color = new Color(before.r, before.g, before.b, before.a * a * (1f - level));
                // One ring for the cross-fading pair, at the tile's full alpha
                // (two rings would darken mid-fade).
                GUI.color = new Color(before.r, before.g, before.b, before.a * a);
                CrispText.Outline(r, s.Label, _idle);
                GUI.color = new Color(before.r, before.g, before.b, before.a * a * (1f - level));
                if (level < 0.99f) CrispText.LabelPlain(r, s.Label, _idle);
                if (level > 0.01f)
                {
                    GUI.color = new Color(before.r, before.g, before.b, before.a * a * level);
                    CrispText.LabelPlain(r, s.Label, _lit);
                }
            }
            if (editing)
            {
                GUI.color = new Color(before.r, before.g, before.b, before.a);
                WinPx.Box(s.Rect, GUIContent.none, UiKit.Outline);
                WinPx.Box(HandleRect(s.Rect), GUIContent.none, UiKit.Handle);
            }
        }

        // Square-cornered, crisp drawing (T-0363): a flat colour on the white
        // texture, no rounded 9-slice edge to blur. Every size below is a
        // constant times the tile's scale, so tile, border, dot and line keep
        // one ratio at every size.
        private const float BorderPx = 1.5f, LineThickness = 2f, DotSize = 8f;

        private static Rect Inset(Rect r, float d)
        {
            return new Rect(r.x + d, r.y + d, Mathf.Max(0f, r.width - d * 2f), Mathf.Max(0f, r.height - d * 2f));
        }

        // Whole screen pixels under the HUD's scale (T-0363): a border is the
        // same width on every tile, never 1 px on one and 2 on the next.
        private static float Fx() { float f = GUI.matrix.m00; return f > 0.01f ? f : 1f; }

        private static float Thick(float v)
        {
            float f = Fx();
            return Mathf.Max(1f, Mathf.Round(v * f)) / f;
        }

        private static Rect SnapRect(Rect r)
        {
            float f = Fx();
            float x0 = Mathf.Round(r.x * f) / f, y0 = Mathf.Round(r.y * f) / f;
            float x1 = Mathf.Round(r.xMax * f) / f, y1 = Mathf.Round(r.yMax * f) / f;
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        private static void Flat(Rect r, Color c) { FlatRaw(SnapRect(r), c); }

        private static void FlatRaw(Rect r, Color c)
        {
            Color keep = GUI.color;
            GUI.color = new Color(c.r * keep.r, c.g * keep.g, c.b * keep.b, c.a * keep.a);
            WinPx.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = keep;
        }

        // The mouse path is one GL quad batch (T-0363): rotated GUI textures and
        // stamped squares both broke up or stacked their alpha in game. One quad
        // per segment, the age fade per vertex, so nothing overlaps along a
        // segment. Hidden/Internal-Colored is a stock shader (DebugDraw uses it).
        private static Material _lineMat;
        private static bool _lineMatTried;
        private bool _lineMatTold;

        private bool EnsureLineMaterial()
        {
            if (_lineMat != null) return true;
            if (!_lineMatTried)
            {
                _lineMatTried = true;
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    _lineMat = new Material(shader);
                    _lineMat.hideFlags = HideFlags.HideAndDontSave;
                    _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _lineMat.SetInt("_ZWrite", 0);
                    _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
            }
            if (_lineMat == null && !_lineMatTold)
            {
                _lineMatTold = true;
                Ctx.Log.LogWarning("Inputs: the mouse path cannot be drawn (Hidden/Internal-Colored not found) - the mouse box shows the dot only.");
            }
            return _lineMat != null;
        }

        private static int _glVerts;   // quads drawn this batch x 4, for the Perf line
        /// One segment as a quad of the given thickness, fading from colour c0 to c1.
        private static void Quad(float x0, float y0, float x1, float y1, float thick, Color c0, Color c1)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            _glVerts += 4;
            float nx = -dy / len * thick * 0.5f, ny = dx / len * thick * 0.5f;
            GL.Color(c0);
            Vertex(x0 + nx, y0 + ny);
            Vertex(x0 - nx, y0 - ny);
            GL.Color(c1);
            Vertex(x1 - nx, y1 - ny);
            Vertex(x1 + nx, y1 + ny);
        }

        /// A HUD point to GL's pixel space (LoadPixelMatrix: origin bottom-left), through the HUD's scale.
        private static void Vertex(float x, float y)
        {
            Vector3 s = GUI.matrix.MultiplyPoint3x4(new Vector3(x, y, 0f));
            GL.Vertex3(s.x, Screen.height - s.y, 0f);
        }

        private readonly float[] _pieces = new float[TrailClip.MaxPieces * TrailClip.PieceStride];

        // The Comet: the head fixed at the tile's centre, the tail drawn relative to it. A tail longer
        // than the tile's room is scaled as a whole (CometPath.FitScale), so its direction and shape stay.
        private void DrawComet(Rect r, float scale, float now, Color before, float a)
        {
            float half = r.width * 0.5f - 8f * scale;
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
            float thick = Thick(LineThickness * scale);
            GUI.color = Color.white;
            int live = CometPath.Live(_cometT, _cometHead, _cometCount, CometMax, now, CometSeconds);
            if (live == 0) { _cometFit = 1f; _cometFitAt = now; }
            if (live > 0 && Event.current.type == EventType.Repaint && EnsureLineMaterial())
            {
                float target = CometPath.FitScale(CometPath.Extent(_cometX, _cometY, _cometT, _cometHead, _cometCount, CometMax, now, CometSeconds, _ux, _uy), 1f);
                _cometFit = CometPath.Ease(_cometFit, target, now - _cometFitAt, 1.5f);
                _cometFitAt = now;
                float fit = half * _cometFit;
                float px = cx, py = cy, pAge = 0f, ba = 0.8f * before.a * a;
                long glStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _glVerts = 0;
                GL.PushMatrix();
                _lineMat.SetPass(0);
                GL.LoadPixelMatrix();
                GL.Begin(GL.QUADS);
                for (int k = 0; k < live; k++)
                {
                    int idx = (_cometHead - 1 - k + CometMax * 2) % CometMax;
                    float age = now - _cometT[idx];
                    float tx = cx + (_cometX[idx] - _ux) * fit, ty = cy + (_cometY[idx] - _uy) * fit;
                    if ((tx - px) * (tx - px) + (ty - py) * (ty - py) < 1f) continue;   // under a pixel: wait, so the path still builds
                    float f0 = CometPath.Fade(pAge, CometSeconds), f1 = CometPath.Fade(age, CometSeconds);
                    Quad(px, py, tx, ty, thick,
                        new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * f0),
                        new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * f1));
                    px = tx; py = ty; pAge = age;
                }
                GL.End();
                GL.PopMatrix();
                DrawTarget.Record(glStart, _glVerts);
            }
            float head = DotSize * scale;
            Flat(new Rect(cx - head * 0.5f, cy - head * 0.5f, head, head), new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, before.a * a));
        }

        private void DrawMouseBox(Rect r, float scale, float now, Color before, float a)
        {
            if (Comet) { DrawComet(r, scale, now, before, a); return; }
            float half = r.width * 0.5f - 8f * scale;
            float cx = r.x + r.width * 0.5f, cy = r.y + r.height * 0.5f;
            float thick = Thick(LineThickness * scale);
            GUI.color = Color.white;   // colours below carry their own alpha
            float hx = cx + _mx * half, hy = cy + _my * half;
            float px = hx, py = hy, pAge = 0f;   // the line runs from the head back along the trail
            bool gl = _trailCount > 0 && Event.current.type == EventType.Repaint && EnsureLineMaterial();
            long glStart = 0;
            if (gl)
            {
                glStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _glVerts = 0;
                GL.PushMatrix();
                _lineMat.SetPass(0);
                GL.LoadPixelMatrix();   // screen pixels; Vertex applies the HUD's scale and flips y (as MapModule)
                GL.Begin(GL.QUADS);
            }
            for (int k = 0; k < _trailCount; k++)
            {
                int idx = (_trailHead - 1 - k + TrailMax * 2) % TrailMax;
                float age = now - _trailT[idx];
                if (age >= TrailSeconds) break;   // older entries are older still
                float tx = cx + _trailX[idx] * half, ty = cy + _trailY[idx] * half;
                // A jump across the box (the mouse wrapped at an edge) is split at the edge by TrailClip.Split.
                if (gl)
                {
                    float f0 = 1f - pAge / TrailSeconds, f1 = 1f - age / TrailSeconds;
                    float ba = 0.8f * before.a * a;
                    int pieces = TrailClip.Split(px, py, tx, ty, cx, cy, half, thick, _pieces);
                    for (int q = 0; q < pieces; q++)
                    {
                        int i = q * TrailClip.PieceStride;
                        float fa = f0 + (f1 - f0) * _pieces[i + 4], fb = f0 + (f1 - f0) * _pieces[i + 5];
                        Quad(_pieces[i], _pieces[i + 1], _pieces[i + 2], _pieces[i + 3], thick,
                            new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * fa),
                            new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, ba * fb));
                    }
                }
                px = tx; py = ty; pAge = age;
            }
            if (gl)
            {
                GL.End();
                GL.PopMatrix();
                DrawTarget.Record(glStart, _glVerts);
            }
            float head = DotSize * scale;
            Flat(new Rect(hx - head * 0.5f, hy - head * 0.5f, head, head), new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, before.a * a));
        }

        // --- the mouse in Edit HUD: drag a tile, right-click to hide / show it ---------------------------

        // The corner handle: drag it to resize the tile in the 0.25 steps the + / - buttons keep.
        private static Rect HandleRect(Rect r)
        {
            float h = Mathf.Min(HandlePx, Mathf.Min(r.width, r.height) * 0.5f);
            return new Rect(r.xMax - h, r.yMax - h, h, h);
        }

        // The grab area is 4 px larger than the square, as the HUD values' handle.
        private static Rect HandleHit(Rect r)
        {
            Rect h = HandleRect(r);
            return new Rect(h.x - 2f, h.y - 2f, h.width + 4f, h.height + 4f);
        }

        private void FinishResize()
        {
            if (_resize < 0) return;
            TileState z = _states[Mathf.Min(_resize, _states.Count - 1)];
            _resize = -1;
            Commit();   // once per gesture
            Ctx.Log.LogInfo("Inputs: tile " + InputLayout.TileName(z.Tile) + " resized by dragging to " + z.Tile.Scale.ToString("0.##") + ".");
        }

        private void EditEvents(UiScale ui, Event e)
        {
            MainWindowModule main = Host.Find<MainWindowModule>();
            Rect blocked = main != null ? Host.Hud.Settings.Virtual(main.ScreenRect) : new Rect();
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (_drag >= 0 || _resize >= 0 || blocked.Contains(e.mousePosition)) return;
                    for (int i = _states.Count - 1; i >= 0; i--)
                    {
                        TileState s = _states[i];
                        if (!s.Valid || !s.Rect.Contains(e.mousePosition)) continue;
                        if (e.button == 0 && HandleHit(s.Rect).Contains(e.mousePosition)) { _resize = i; _resizeStartScale = s.Tile.Scale; _resizeStartMouse = e.mousePosition; e.Use(); return; }
                        if (e.button == 1) { SetShown(s, !s.Tile.Shown); e.Use(); return; }
                        if (e.button != 0) return;
                        _drag = i;
                        _dragOffset = e.mousePosition - new Vector2(s.Rect.x, s.Rect.y);
                        e.Use();
                        return;
                    }
                    break;
                case EventType.MouseDrag:
                    if (_resize >= 0 && _resize < _states.Count)
                    {
                        TileState rs = _states[_resize];
                        float v = InputLayout.ScaleFromDrag(rs.Tile.Key, _resizeStartScale, _resizeStartMouse.x, _resizeStartMouse.y,
                            e.mousePosition.x, e.mousePosition.y, rs.Rect.x, rs.Rect.y, ui.VW, ui.VH);
                        if (v != rs.Tile.Scale) rs.Tile.Scale = v;
                        e.Use();
                        return;
                    }
                    if (_drag < 0 || _drag >= _states.Count) return;
                    TileState d = _states[_drag];
                    float x = Mathf.Clamp(e.mousePosition.x - _dragOffset.x, 0f, Mathf.Max(0f, ui.VW - d.Rect.width));
                    float y = Mathf.Clamp(e.mousePosition.y - _dragOffset.y, 0f, Mathf.Max(0f, ui.VH - d.Rect.height));
                    Vector2 at = Host.Hud.Widgets.SnapDrag(d.SnapId, new Rect(x, y, d.Rect.width, d.Rect.height));
                    d.Rect = new Rect(at.x, at.y, d.Rect.width, d.Rect.height);
                    d.Tile.X = ui.ToRefX(at.x);
                    d.Tile.Y = ui.ToRefY(at.y);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (_resize >= 0) { FinishResize(); e.Use(); return; }
                    if (_drag < 0) return;
                    TileState m = _states[Mathf.Min(_drag, _states.Count - 1)];
                    _drag = -1;
                    Host.Hud.Widgets.EndSnap();
                    m.Tile.X = Mathf.Round(m.Tile.X);
                    m.Tile.Y = Mathf.Round(m.Tile.Y);
                    Commit();   // once per gesture
                    Ctx.Log.LogInfo("Inputs: tile " + InputLayout.TileName(m.Tile) + " moved to (" + Mathf.RoundToInt(m.Tile.X) + ", " + Mathf.RoundToInt(m.Tile.Y) + ").");
                    e.Use();
                    break;
            }
        }

        // --- the Edit HUD rows -----------------------------------------------------------------------

        private static readonly GUIContent InputsText = new GUIContent("Input overlay");
        private static readonly GUIContent About = new GUIContent(
            "Shows the keys and mouse buttons you press, on the game screen. Each tile is placed on its own: " +
            "drag it to move it, drag its corner square to resize it, right-click it to hide or show it. Nothing shows while a cursor is on screen.");
        private static readonly GUIContent OnlyPressedText = new GUIContent("Only while pressed");
        private static readonly GUIContent OnlyPressedAbout = new GUIContent(
            "A key or button tile is drawn only while it is held, exactly as pressed, and not at all otherwise. The mouse is drawn on frames it moves (the Comet also while its tail is still fading) and the wheel only on frames it scrolls. This window still shows every tile so you can place them.");
        private static readonly GUIContent AddKeyText = new GUIContent("Add input");
        private static readonly GUIContent AddKeyWaiting = new GUIContent("Press an input...");
        private static readonly GUIContent AddKeyTip = new GUIContent(
            "Press any key, a mouse button 3-7 or turn the wheel and it becomes a tile. Esc cancels. M1 and M2 are not taken here (a click would be the press): a removed M1 or M2 has its own add button.");
        private static readonly GUIContent AddMouseBoxText = new GUIContent("+ Mouse box");
        private static readonly GUIContent AddMouseBoxTip = new GUIContent("The mouse's movement as a fading trail (style: Comet or Box, set above).");
        private static readonly GUIContent TilesTitle = new GUIContent("Tiles");
        private static readonly GUIContent RemoveSureText = new GUIContent("Sure?");
        private static readonly GUIContent AddM1Text = new GUIContent("+ M1");
        private static readonly GUIContent AddM2Text = new GUIContent("+ M2");
        private static readonly GUIContent ResetText = new GUIContent("Default layout");
        private static readonly GUIContent ResetSureText = new GUIContent("Sure?");
        private static readonly GUIContent ResetTip = new GUIContent("WASD, Shift, Space, Ctrl, M1, M2 and the mouse box, back where they started.");
        private static readonly GUIContent RemoveText = new GUIContent("x");
        private static readonly GUIContent RemoveTip = new GUIContent("Remove this tile: click twice (hide it instead to keep its place).");
        private static readonly GUIContent BrokenText = new GUIContent(
            "The game gave no mouse movement, so the mouse box stays still.");
        private TileState _removeSure;   // the row whose x was clicked once: the next click removes it
        private float _removeSureUntil;

        public override float DrawHudEditor(float y, float w)
        {
            const float indent = 20f;
            Rect onR = new Rect(0f, y, w, 22f);
            bool on = UiKit.Toggle(onR, _on.Value, InputsText);
            UiKit.Hint(onR, About);
            if (on != _on.Value)
            {
                _on.Value = on;
                Ctx.Log.LogInfo("Inputs: " + (on ? "on" : "off") + ".");
            }
            y += 24f;
            if (!_on.Value) return y + 8f;

            y = DrawSourceChoice(y, w, indent);

            y = DrawStyleChoice(y, w, indent);

            Rect onlyR = new Rect(0f, y, w, 22f);
            bool only = UiKit.Toggle(onlyR, _onlyPressed.Value, OnlyPressedText);
            UiKit.Hint(onlyR, OnlyPressedAbout);
            if (only != _onlyPressed.Value)
            {
                _onlyPressed.Value = only;
                Ctx.Log.LogInfo("Inputs: only-pressed mode " + (only ? "on" : "off") + ".");
            }
            y += 28f;

            if (UiKit.Section(indent, ref y, w - indent, "hud.inputtiles", TilesTitle, false))
                y = DrawTiles(indent, y, w - indent);
            if (_status.text.Length > 0) y += UiText.Draw(0f, y, w, _status, UiKit.HintStyle) + 6f;

            bool armed = _resetArmed && Time.unscaledTime <= _resetUntil;
            Rect resetR = new Rect(0f, y, 120f, 24f);
            if (WinPx.Button(resetR, armed ? ResetSureText : ResetText))
            {
                if (armed) { _resetArmed = false; ResetLayout(); }
                else { _resetArmed = true; _resetUntil = Time.unscaledTime + 4f; }
            }
            UiKit.Hint(resetR, ResetTip);
            y += 30f;
            if (_axisBroken) y += UiText.Draw(0f, y, w, BrokenText, UiKit.HintStyle) + 2f;
            return y + 8f;
        }

        private float DrawTiles(float x0, float y, float w)
        {
            _removeAt = -1;
            for (int i = 0; i < _states.Count; i++) y = DrawTileRow(x0, i, y, w);
            if (_removeAt >= 0 && _removeAt < _states.Count) RemoveTile(_removeAt);   // after the rows: the list changes
            _removeAt = -1;

            // Add controls, flowing left to right; one that is already present is not drawn.
            float x = 0f;
            float keyW = Mathf.Max(90f, WinPx.CalcSize(GUI.skin.button, AddKeyWaiting).x + 8f, WinPx.CalcSize(GUI.skin.button, AddKeyText).x + 8f);
            Rect keyR = new Rect(x0 + x, y, keyW, 24f);
            if (UiKit.Toggle(keyR, _capturing, _capturing ? AddKeyWaiting : AddKeyText, GUI.skin.button) != _capturing)
            {
                _capturing = !_capturing;
                if (_capturing) Host.Hotkeys.HoldCapture(); else Host.Hotkeys.ReleaseCapture();
                SetStatus(_capturing ? "Press a key, mouse button 3-7 or turn the wheel. Esc cancels." : "");
            }
            UiKit.Hint(keyR, AddKeyTip);
            x += keyW + 6f;
            if (_layout.Find("Mouse0") == null) y = AddButton(x0, ref x, y, w, AddM1Text, null, "Mouse0");
            if (_layout.Find("Mouse1") == null) y = AddButton(x0, ref x, y, w, AddM2Text, null, "Mouse1");
            if (_layout.Find(InputLayout.MouseKey) == null) y = AddButton(x0, ref x, y, w, AddMouseBoxText, AddMouseBoxTip, InputLayout.MouseKey);
            return y + 30f;
        }

        private static readonly GUIContent StyleTitle = new GUIContent("Mouse");
        private static readonly GUIContent StyleCometText = new GUIContent("Comet");
        private static readonly GUIContent StyleBoxText = new GUIContent("Box");
        private static readonly GUIContent StyleCometTip = new GUIContent(
            "No frame: the dot stays put and a tail of the last 0.4 s shows where the mouse came from.");
        private static readonly GUIContent StyleBoxTip = new GUIContent(
            "A framed box where the dot drifts and wraps at the edges.");

        private float DrawStyleChoice(float y, float w, float indent)
        {
            const float labelW = 60f;
            WinPx.Label(new Rect(0f, y, labelW, 22f), StyleTitle);
            float bw = Mathf.Max(60f, (w - labelW - 4f) * 0.5f);
            bool comet = Comet;
            Rect cometR = new Rect(labelW, y, bw, 22f), boxR = new Rect(labelW + bw + 4f, y, bw, 22f);
            if (UiKit.Toggle(cometR, comet, StyleCometText, GUI.skin.button) && !comet) SetStyle(StyleComet);
            if (UiKit.Toggle(boxR, !comet, StyleBoxText, GUI.skin.button) && comet) SetStyle(StyleBox);
            UiKit.Hint(cometR, StyleCometTip);
            UiKit.Hint(boxR, StyleBoxTip);
            return y + 30f;
        }

        private void SetStyle(string style)
        {
            _mouseStyle.Value = style;
            _trailCount = 0;
            _cometCount = 0;
            Ctx.Log.LogInfo("Inputs: mouse style " + (style == StyleBox ? "box" : "comet"));
        }

        private static readonly GUIContent SourceTitle = new GUIContent("Shows");
        private static readonly GUIContent SourceGameText = new GUIContent("Game input");
        private static readonly GUIContent SourceRawText = new GUIContent("Raw input");
        private static readonly GUIContent SourceAbout = new GUIContent(
            "Game input lights a tile when its game action fires; Raw input when you press its key.");
        private static readonly GUIContent SourceGameTip = new GUIContent(
            "Each tile is its key's game action (Jump, Crouch, Forward...), lit whenever the game's action fires, so a rebind follows the action and a replay's or the test bridge's inputs show. The mouse wheel is raw in both. This window always lights tiles from the keys.");
        private static readonly GUIContent SourceRawTip = new GUIContent(
            "The keys you press, named as keys. The mouse wheel is raw in both. This window always lights tiles from the keys.");
        private readonly GUIContent _sourceStatus = new GUIContent("");
        private string _statusFor;

        private float DrawSourceChoice(float y, float w, float indent)
        {
            const float labelW = 60f;
            WinPx.Label(new Rect(0f, y, labelW, 22f), SourceTitle);
            float bw = Mathf.Max(60f, (w - labelW - 4f) * 0.5f);
            Rect gameR = new Rect(labelW, y, bw, 22f), rawR = new Rect(labelW + bw + 4f, y, bw, 22f);
            bool game = GameInput;
            if (UiKit.Toggle(gameR, game, SourceGameText, GUI.skin.button) && !game) SetSource(SourceGame);
            if (UiKit.Toggle(rawR, !game, SourceRawText, GUI.skin.button) && game) SetSource(SourceRaw);
            UiKit.Hint(gameR, SourceGameTip);
            UiKit.Hint(rawR, SourceRawTip);
            y += 26f;
            y += UiText.Draw(indent, y, w - indent, SourceAbout, UiKit.HintStyle) + 2f;
            if (GameInput && !GameActions.Ready)
            {
                if (!ReferenceEquals(_statusFor, GameActions.Status))
                {
                    _statusFor = GameActions.Status;
                    _sourceStatus.text = "The game's actions are not readable (" + _statusFor + "): the tiles read the keys.";
                }
                y += UiText.Draw(indent, y, w - indent, _sourceStatus, UiKit.HintStyle) + 2f;
            }
            return y + 2f;
        }

        private void SetSource(string source)
        {
            if (_source.Value == source) return;
            _source.Value = source;
            ApplySource();
            Ctx.Log.LogInfo("Inputs: source " + (source == SourceRaw ? "raw input" : "game input"));
        }

        private float AddButton(float x0, ref float x, float y, float w, GUIContent text, GUIContent tip, string key)
        {
            float bw = Mathf.Max(80f, WinPx.CalcSize(GUI.skin.button, text).x + 8f);   // as wide as its words, disabled or not
            if (x + bw > w && x > 0f) { x = 0f; y += 28f; }
            Rect r = new Rect(x0 + x, y, bw, 24f);
            if (WinPx.Button(r, text)) AddTile(key);
            if (tip != null) UiKit.Hint(r, tip);
            x += bw + 6f;
            return y;
        }

        // [shown] name ........ [x]  (a second click on the x removes; the corner square resizes)
        private float DrawTileRow(float x0, int i, float y, float w)
        {
            TileState s = _states[i];
            if (s.Skip) return y;   // a per-action tile not shown now (raw input)
            const float gap = 4f;
            float removeW = Mathf.Max(46f, WinPx.CalcSize(GUI.skin.button, RemoveSureText).x + 8f);   // wide enough for the confirm word
            float h = 22f;
            bool sure = ReferenceEquals(_removeSure, s) && Time.unscaledTime <= _removeSureUntil;
            Rect removeR = new Rect(x0 + w - removeW, y, removeW, h);
            if (WinPx.Button(removeR, sure ? RemoveSureText : RemoveText))
            {
                if (sure) { _removeSure = null; _removeAt = i; }
                else { _removeSure = s; _removeSureUntil = Time.unscaledTime + 4f; }
            }
            UiKit.Hint(removeR, RemoveTip);
            Rect nameR = new Rect(x0, y, Mathf.Max(40f, w - removeW - gap), h);
            bool shown = UiKit.Toggle(nameR, s.Tile.Shown, s.Name);
            if (shown != s.Tile.Shown) SetShown(s, shown);
            return y + 26f;
        }
    }
}
