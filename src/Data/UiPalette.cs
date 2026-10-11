namespace ForestOverlay.Data
{
    /// The window's colours: yellow on black, as the game's loading screen
    /// and the site (decisions.md *Look: yellow on black*). T-0258: the
    /// author picks one of the variants in game (Developer -> Colours); each
    /// is a full set of the tokens UiKit draws with, as 0xRRGGBBAA.
    public sealed class UiPalette
    {
        /// The loading screen's progress bar and the logo: rgb(229, 197, 1).
        public const uint Yellow = 0xE5C501FF;

        public string Id;
        public string Name;
        public string Description;

        public uint Panel, Card, CardHover, Border;
        public uint ButtonBorder, ButtonHoverBorder, Field;
        public uint Widget, WidgetBorder, Tip, TipBorder;
        /// Text: body text. Dim: hints, section summaries, HUD labels.
        /// Soft: descriptions (UiText.Dim), between the two.
        public uint Text, Dim, Soft;
        public uint Accent, AccentHover, AccentDark, OnAccent, Warn;

        public static readonly UiPalette[] All =
        {
            // The site's own tokens (site/ForestSite/wwwroot/style.css): black,
            // #0D0D0D panels, #222 lines, yellow text, #8A7A1C dim.
            new UiPalette
            {
                Id = "site", Name = "The site's colours",
                Description = "Exactly forest.deter.cloud: black, yellow text, dim yellow hints.",
                Panel = 0x000000FF, Card = 0x0D0D0DFF, CardHover = 0x1A1A1AFF, Border = 0x222222FF,
                ButtonBorder = 0x2A2A2AFF, ButtonHoverBorder = 0x8A7A1CFF, Field = 0x050505FF,
                Widget = 0x000000D6, WidgetBorder = 0x222222E6, Tip = 0x0D0D0DFA, TipBorder = 0x8A7A1CFF,
                // Text one alpha step off Accent, Soft one step above Dim: the
                // same to the eye, but UiKit.Apply tells text colours apart by value.
                Text = 0xE5C501FE, Dim = 0x8A7A1CFF, Soft = 0x8B7B1DFF,
                Accent = Yellow, AccentHover = 0xF2D640FF, AccentDark = 0x4A3F00FF, OnAccent = 0x000000FF,
                Warn = 0xFF8C33FF,
            },
            new UiPalette
            {
                Id = "black", Name = "Pure black",
                Description = "Black panels, off-white text, yellow only for the accent.",
                Panel = 0x000000FF, Card = 0x121212FF, CardHover = 0x1C1C1CFF, Border = 0x2A2A2AFF,
                ButtonBorder = 0x333333FF, ButtonHoverBorder = 0x4A4A4AFF, Field = 0x080808FF,
                Widget = 0x000000D6, WidgetBorder = 0x262626E6, Tip = 0x050505FA, TipBorder = 0x3A3A3AFF,
                Text = 0xEAEAEAFF, Dim = 0x8C8C8CFF, Soft = 0xC2C2C2FF,
                Accent = Yellow, AccentHover = 0xF2D640FF, AccentDark = 0x4A3F00FF, OnAccent = 0x000000FF,
                Warn = 0xFF8C33FF,
            },
            // The box art (tasks/notes/T-0258.md): near-black with a faint
            // olive / brown tint, never blue-grey.
            new UiPalette
            {
                Id = "warm", Name = "Warm near-black",
                Description = "The box art's dark woods: near-black with an olive tint, warm off-white text.",
                Panel = 0x0E0E09FF, Card = 0x18170FFF, CardHover = 0x222016FF, Border = 0x2C291DFF,
                ButtonBorder = 0x373323FF, ButtonHoverBorder = 0x4E4831FF, Field = 0x0A0A06FF,
                Widget = 0x0B0B07D6, WidgetBorder = 0x2C291DE6, Tip = 0x080805FA, TipBorder = 0x3E3A27FF,
                Text = 0xECE6D4FF, Dim = 0x9A927AFF, Soft = 0xC8C0A8FF,
                Accent = Yellow, AccentHover = 0xF2D640FF, AccentDark = 0x4A3F00FF, OnAccent = 0x000000FF,
                Warn = 0xFF8C33FF,
            },
            new UiPalette
            {
                Id = "translucent", Name = "Translucent black",
                Description = "Black at about 80 %: the game shows through the window.",
                Panel = 0x000000C8, Card = 0x1A1A1AB4, CardHover = 0x262626CC, Border = 0x333333E6,
                ButtonBorder = 0x3A3A3AE6, ButtonHoverBorder = 0x505050F0, Field = 0x000000B4,
                Widget = 0x00000099, WidgetBorder = 0x333333B3, Tip = 0x000000F0, TipBorder = 0x3A3A3AFF,
                Text = 0xEAEAEAFF, Dim = 0x9A9A9AFF, Soft = 0xC8C8C8FF,
                Accent = Yellow, AccentHover = 0xF2D640FF, AccentDark = 0x4A3F00E6, OnAccent = 0x000000FF,
                Warn = 0xFF8C33FF,
            },
        };

        /// The site's colours until the author picks (decisions.md: "the
        /// same palette as the site").
        public static UiPalette Default { get { return All[0]; } }

        /// The variant with this id; Default for an unknown or empty one.
        public static UiPalette Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return Default;
        }

        public static float R(uint c) { return ((c >> 24) & 0xFF) / 255f; }
        public static float G(uint c) { return ((c >> 16) & 0xFF) / 255f; }
        public static float B(uint c) { return ((c >> 8) & 0xFF) / 255f; }
        public static float A(uint c) { return (c & 0xFF) / 255f; }
    }
}
