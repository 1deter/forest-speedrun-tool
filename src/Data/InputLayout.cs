using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// One on-screen input: a key (a Unity KeyCode name), the mouse box
    /// ("Mouse") or the wheel ("Wheel"), placed on its own.
    public sealed class InputTile
    {
        public string Key = "";
        /// "" for the key's own (first) tile; else the name of the game action this
        /// tile is for (T-0409: a key that serves several actions has one tile per action).
        public string Act = "";
        /// Top-left in reference pixels (like the HUD's placed values).
        public float X, Y;
        public float Scale = 1f;
        public bool Shown = true;
    }

    // ------------------------------------------------------------------
    // The input overlay's layout (T-0302): a list of tiles, each placed on
    // its own anywhere (author, 2026-10-11: "every tile placeable on its own,
    // not a fixed grid"). Kept as one line in a HUD-profile config entry:
    //   W,88,780,1,1;LeftShift,40,876,1,0;Mouse,360,828,1,1
    //   key, x, y, scale, shown(1/0), [action]
    // A tile for one more action of the same key has the action's name as a sixth
    // field (C,300,800,1,1,Batch). The last segment "!split" says the extra tiles
    // were added once; a layout without it gets them when the game's actions are known.
    // Forgiving like HudLayout: a bad tile is skipped, numbers clamped, a
    // second tile for the same key dropped. Pure; linked into the tests.
    // ------------------------------------------------------------------
    public sealed class InputLayout
    {
        public const string SplitMarker = "!split";
        public const string MouseKey = "Mouse", WheelKey = "Wheel";
        public const float MinScale = 0.5f, MaxScale = 3f;
        /// A 1x1 tile's side and the pitch of the default layout, in reference pixels.
        public const float Unit = 44f, Pitch = 48f;
        public const int MaxTiles = 64, MaxKeyLength = 24;

        public readonly List<InputTile> Tiles = new List<InputTile>();

        /// True once the per-action tiles were added (or chosen against); saved with the layout.
        public bool Split;

        public InputTile Find(string key)
        {
            return Find(key, "");
        }

        public InputTile Find(string key, string act)
        {
            for (int i = 0; i < Tiles.Count; i++)
                if (string.Equals(Tiles[i].Key, key, StringComparison.Ordinal) && string.Equals(Tiles[i].Act, act, StringComparison.Ordinal)) return Tiles[i];
            return null;
        }

        /// Adds a tile at (x, y); null when the key is bad, already there or the list is full.
        public InputTile Add(string key, float x, float y)
        {
            return Add(key, "", x, y);
        }

        public InputTile Add(string key, string act, float x, float y)
        {
            if (act == null) act = "";
            if (!ValidKey(key) || (act.Length > 0 && !ValidKey(act)) || Tiles.Count >= MaxTiles || Find(key, act) != null) return null;
            InputTile t = new InputTile();
            t.Key = key;
            t.Act = act;
            t.X = x;
            t.Y = y;
            Tiles.Add(t);
            return t;
        }

        /// Where a new tile goes (T-0355): the first slot, scanning rows up from the
        /// bottom and columns left from RingClear px short of the right edge (the
        /// game's own health ring is bottom right), that overlaps no tile already
        /// there and stays fully on screen. The default block is bottom centre, so
        /// the slots start right of it. Falls back to the first slot if all are taken.
        public const float RingClear = 280f;

        public void FreeSlot(string key, int refW, int refH, out float x, out float y)
        {
            float w = TileW(key, 1f), h = TileH(key, 1f);
            float bottom = refH - Math.Max(16f, refH * 0.03f);
            float right = refW - RingClear;
            float left = (refW + DefaultWidth) * 0.5f + Pitch;
            if (left > right - w) left = Math.Max(0f, right - w);
            x = right - w; y = bottom - h;
            bool first = true;
            for (float sy = bottom - h; sy >= 0f; sy -= Pitch)
                for (float sx = right - w; sx >= left; sx -= Pitch)
                {
                    if (first) { x = sx; y = sy; first = false; }
                    if (IsFree(sx, sy, w, h)) { x = sx; y = sy; return; }
                }
        }

        private bool IsFree(float x, float y, float w, float h)
        {
            for (int i = 0; i < Tiles.Count; i++)
            {
                InputTile t = Tiles[i];
                float tw = TileW(t.Key, t.Scale), th = TileH(t.Key, t.Scale);
                if (x < t.X + tw && t.X < x + w && y < t.Y + th && t.Y < y + h) return false;
            }
            return true;
        }

        /// Removes the key's own tile and its per-action tiles.
        public bool Remove(string key)
        {
            bool any = false;
            for (int i = Tiles.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(Tiles[i].Key, key, StringComparison.Ordinal)) continue;
                Tiles.RemoveAt(i);
                any = true;
            }
            return any;
        }

        /// Removes one tile: a per-action tile alone, or (act "") the whole key.
        public bool Remove(string key, string act)
        {
            if (string.IsNullOrEmpty(act)) return Remove(key);
            for (int i = 0; i < Tiles.Count; i++)
            {
                if (!string.Equals(Tiles[i].Key, key, StringComparison.Ordinal) || !string.Equals(Tiles[i].Act, act, StringComparison.Ordinal)) continue;
                Tiles.RemoveAt(i);
                return true;
            }
            return false;
        }

        // --- one tile per action (T-0409) ------------------------------------------

        /// Gives every key tile its per-action tiles (the key's 2nd, 3rd... action in
        /// pick order) next to it, those not already there, then marks the layout done.
        /// Returns how many were added.
        public int SplitAll(InputActionTable table, int refW, int refH)
        {
            int n = Tiles.Count, added = 0;
            for (int i = 0; i < n; i++)
            {
                InputTile t = Tiles[i];
                if (t.Act.Length == 0) added += SplitKey(t, table, refW, refH);
            }
            Split = true;
            return added;
        }

        /// The per-action tiles of one key tile, next to it; how many were added.
        public int SplitKey(InputTile baseTile, InputActionTable table, int refW, int refH)
        {
            if (baseTile.Key == MouseKey || baseTile.Key == WheelKey) return 0;
            int[] acts = new int[InputActionTable.MaxPerKey], signs = new int[InputActionTable.MaxPerKey];
            int count = table.Resolve(baseTile.Key, acts, signs);
            int added = 0;
            for (int k = 1; k < count; k++)
            {
                string name = table.NameOf(acts[k]);
                if (!ValidKey(name) || Find(baseTile.Key, name) != null || Tiles.Count >= MaxTiles) continue;
                float x, y;
                NearSlot(baseTile, refW, refH, out x, out y);
                InputTile t = Add(baseTile.Key, name, x, y);
                if (t == null) continue;
                t.Scale = baseTile.Scale;
                added++;
            }
            return added;
        }

        /// The first free place beside a tile (right, left, below, above; then one step
        /// further out), on screen; the bottom-right free slot if none is near.
        public void NearSlot(InputTile around, int refW, int refH, out float x, out float y)
        {
            float w = TileW(around.Key, around.Scale), h = TileH(around.Key, around.Scale);
            float step = Pitch * around.Scale;
            float gap = (Pitch - Unit) * around.Scale;
            for (int r = 0; r < 6; r++)
            {
                float d = r * step;
                for (int dir = 0; dir < 4; dir++)
                {
                    float cx = around.X, cy = around.Y;
                    if (dir == 0) cx = around.X + w + gap + d;
                    else if (dir == 1) cx = around.X - w - gap - d;
                    else if (dir == 2) cy = around.Y + h + gap + d;
                    else cy = around.Y - h - gap - d;
                    if (cx < 0f || cy < 0f || cx + w > refW || cy + h > refH) continue;
                    if (IsFree(cx, cy, w, h)) { x = cx; y = cy; return; }
                }
            }
            FreeSlot(around.Key, refW, refH, out x, out y);
        }

        public static bool ValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        public static float ClampScale(float s)
        {
            if (float.IsNaN(s)) return 1f;
            return s < MinScale ? MinScale : s > MaxScale ? MaxScale : s;
        }

        // --- the tile's look -------------------------------------------------

        /// Width in 1x1 tiles: the key's own width, or wider when its label would not fit
        /// (never clipped text: "Wheel", "PageDown", "LeftBracket").
        public static float UnitsW(string key)
        {
            float u = BaseUnitsW(key);
            string label = LabelFor(key);
            if (label.Length == 0) return u;
            float need = (float)Math.Ceiling((label.Length * 8f + 12f) / Pitch * 2f) / 2f;
            return need > u ? need : u;
        }

        private static float BaseUnitsW(string key)
        {
            switch (key)
            {
                case MouseKey: return 2f;
                case "Space": return 3f;
                case "LeftShift": case "RightShift": case "LeftControl": case "RightControl":
                case "LeftAlt": case "RightAlt": case "Tab": case "CapsLock": case "Return": case "Backspace":
                    return 1.5f;
                default: return 1f;
            }
        }

        /// A tile's size in reference pixels at its scale.
        public static float TileW(string key, float scale)
        {
            return (UnitsW(key) * Pitch - (Pitch - Unit)) * scale;
        }

        /// The scale a dragged corner gives (Edit HUD). Measured from the drag's start: the tile was
        /// startScale big with its corner grabbed at (startMouse), the mouse is now at (mouse). Snaps to
        /// 0.25, never past the screen edge (the tile's origin is at originX / originY, the screen is
        /// vw x vh) and stays within MinScale..MaxScale. A tiny move keeps the scale.
        public static float ScaleFromDrag(string key, float startScale, float startMouseX, float startMouseY,
            float mouseX, float mouseY, float originX, float originY, float vw, float vh)
        {
            float w1 = TileW(key, 1f), h1 = TileH(key, 1f);
            float newW = TileW(key, startScale) + (mouseX - startMouseX);
            float newH = TileH(key, startScale) + (mouseY - startMouseY);
            float want = (newW / w1 + newH / h1) * 0.5f;
            float fit = Math.Min((vw - originX) / w1, (vh - originY) / h1);
            float v = (float)Math.Round(Math.Min(want, fit) * 4.0) / 4f;
            while (v > fit + 0.001f && v > MinScale) v -= 0.25f;
            return ClampScale(v);
        }

        public static float TileH(string key, float scale)
        {
            return (UnitsH(key) * Pitch - (Pitch - Unit)) * scale;
        }

        public static float UnitsH(string key)
        {
            return key == MouseKey ? 2f : 1f;
        }

        /// The words on a tile: the key's name, shortened.
        public static string LabelFor(string key)
        {
            switch (key)
            {
                case "LeftShift": return "Shift";
                case "RightShift": return "R-Shift";
                case "LeftControl": return "Ctrl";
                case "RightControl": return "R-Ctrl";
                case "LeftAlt": return "Alt";
                case "RightAlt": return "R-Alt";
                case "Space": return "Space";
                case "Return": return "Enter";
                case "Backspace": return "Bksp";
                case "CapsLock": return "Caps";
                case "UpArrow": return "Up";
                case "DownArrow": return "Down";
                case "LeftArrow": return "Left";
                case "RightArrow": return "Right";
                case "Mouse0": return "M1";
                case "Mouse1": return "M2";
                case "Mouse2": return "M3";
                case "Mouse3": return "M4";
                case "Mouse4": return "M5";
                case "Mouse5": return "M6";
                case "Mouse6": return "M7";
                case WheelKey: return "Wheel";
                case MouseKey: return "";
            }
            if (key.Length == 6 && key.StartsWith("Alpha", StringComparison.Ordinal)) return key.Substring(5);
            if (key.Length == 7 && key.StartsWith("Keypad", StringComparison.Ordinal)) return "N" + key.Substring(6);
            return key;
        }

        // --- file ---------------------------------------------------------------

        /// The default layout: WASD, Shift, Space, Ctrl, M1, M2 and the mouse box.
        public static InputLayout Default()
        {
            return Default(1920, 1080);
        }

        /// The default layout for a reference screen (Data/UiScale: the screen the
        /// install first ran on): bottom centre, whatever its size - the
        /// bottom-left corner is the game's own messages and the always-on
        /// lines (author, 2026-10-15, T-0325).
        public static InputLayout Default(int refW, int refH)
        {
            if (refW < 640) refW = 640;
            if (refH < 480) refH = 480;
            return Block((refW - DefaultWidth) * 0.5f, refW, refH);
        }

        /// The pre-T-0325 default (the bottom-left corner): a saved layout
        /// still exactly this moves to the new default once (author,
        /// 2026-10-15); anything a runner placed stays.
        public static bool IsOldDefault(string text, int refW, int refH)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (refW < 640) refW = 640;
            if (refH < 480) refH = 480;
            string old = Block(Math.Max(16f, refW * 0.02f), refW, refH).Format();
            return Parse(text).Format() == old;
        }

        private static InputLayout Block(float ox, int refW, int refH)
        {
            InputLayout l = new InputLayout();
            float oy = refH - (2f * Pitch + Unit) - Math.Max(16f, refH * 0.03f);
            l.Place("W", ox, oy, 1f, 0f);
            l.Place("A", ox, oy, 0f, 1f);
            l.Place("S", ox, oy, 1f, 1f);
            l.Place("D", ox, oy, 2f, 1f);
            l.Place("LeftShift", ox, oy, 0f, 2f);
            l.Place("Space", ox, oy, 1.5f, 2f);
            l.Place("LeftControl", ox, oy, 4.5f, 2f);
            l.Place("Mouse0", ox, oy, 7f, 0f);
            l.Place("Mouse1", ox, oy, 8f, 0f);
            l.Place(MouseKey, ox, oy, 7f, 1f);
            return l;
        }

        /// The default block's width: the mouse box (2 units) from column 7.
        public const float DefaultWidth = 9f * Pitch - (Pitch - Unit);

        private void Place(string key, float ox, float oy, float col, float row)
        {
            Add(key, ox + col * Pitch, oy + row * Pitch);
        }

        /// A layout string saved at another reference screen, its positions converted (T-0327).
        public static string ConvertPositions(string text, int fw, int fh, int tw, int th)
        {
            InputLayout l = Parse(text);
            for (int i = 0; i < l.Tiles.Count; i++)
            {
                l.Tiles[i].X = ProfileUnits.Convert(l.Tiles[i].X, ProfileUnit.X, fw, fh, tw, th);
                l.Tiles[i].Y = ProfileUnits.Convert(l.Tiles[i].Y, ProfileUnit.Y, fw, fh, tw, th);
                // Sizes follow the screen's height, as the other profile sizes do.
                l.Tiles[i].Scale = ClampScale(ProfileUnits.Convert(l.Tiles[i].Scale * 1000f, ProfileUnit.Size, fw, fh, tw, th) / 1000f);
            }
            return l.Format();
        }

        public static InputLayout Parse(string text)
        {
            InputLayout l = new InputLayout();
            if (string.IsNullOrEmpty(text)) return l;
            string[] tiles = text.Split(';');
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i].Trim() == SplitMarker) { l.Split = true; continue; }
                string[] f = tiles[i].Trim().Split(',');
                if (f.Length < 3 || !ValidKey(f[0].Trim())) continue;
                float x, y;
                if (!Num(f[1], out x) || !Num(f[2], out y)) continue;
                string act = f.Length > 5 ? f[5].Trim() : "";
                InputTile t = l.Add(f[0].Trim(), act, Clamp(x), Clamp(y));
                if (t == null) continue;
                float s;
                if (f.Length > 3 && Num(f[3], out s)) t.Scale = ClampScale(s);
                if (f.Length > 4) t.Shown = f[4].Trim() != "0";
            }
            return l;
        }

        private static float Clamp(float v)
        {
            return v < 0f ? 0f : v > 20000f ? 20000f : v;
        }

        private static bool Num(string s, out float v)
        {
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public string Format()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Tiles.Count; i++)
            {
                InputTile t = Tiles[i];
                if (i > 0) sb.Append(';');
                sb.Append(t.Key).Append(',')
                  .Append(Math.Round(t.X).ToString("0", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Math.Round(t.Y).ToString("0", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Math.Round(t.Scale, 2).ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                  .Append(t.Shown ? '1' : '0');
                if (t.Act.Length > 0) sb.Append(',').Append(t.Act);
            }
            if (Split) sb.Append(Tiles.Count > 0 ? ";" : "").Append(SplitMarker);
            return sb.ToString();
        }
    }
}
