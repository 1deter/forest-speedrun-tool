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
    //   key, x, y, scale, shown(1/0)
    // Forgiving like HudLayout: a bad tile is skipped, numbers clamped, a
    // second tile for the same key dropped. Pure; linked into the tests.
    // ------------------------------------------------------------------
    public sealed class InputLayout
    {
        public const string MouseKey = "Mouse", WheelKey = "Wheel";
        public const float MinScale = 0.5f, MaxScale = 3f;
        /// A 1x1 tile's side and the pitch of the default layout, in reference pixels.
        public const float Unit = 44f, Pitch = 48f;
        public const int MaxTiles = 40, MaxKeyLength = 24;

        public readonly List<InputTile> Tiles = new List<InputTile>();

        public InputTile Find(string key)
        {
            for (int i = 0; i < Tiles.Count; i++)
                if (string.Equals(Tiles[i].Key, key, StringComparison.Ordinal)) return Tiles[i];
            return null;
        }

        /// Adds a tile at (x, y); null when the key is bad, already there or the list is full.
        public InputTile Add(string key, float x, float y)
        {
            if (!ValidKey(key) || Tiles.Count >= MaxTiles || Find(key) != null) return null;
            InputTile t = new InputTile();
            t.Key = key;
            t.X = x;
            t.Y = y;
            Tiles.Add(t);
            return t;
        }

        public bool Remove(string key)
        {
            for (int i = 0; i < Tiles.Count; i++)
            {
                if (!string.Equals(Tiles[i].Key, key, StringComparison.Ordinal)) continue;
                Tiles.RemoveAt(i);
                return true;
            }
            return false;
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
                string[] f = tiles[i].Trim().Split(',');
                if (f.Length < 3 || !ValidKey(f[0].Trim())) continue;
                float x, y;
                if (!Num(f[1], out x) || !Num(f[2], out y)) continue;
                InputTile t = l.Add(f[0].Trim(), Clamp(x), Clamp(y));
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
            }
            return sb.ToString();
        }
    }
}
