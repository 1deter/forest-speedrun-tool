namespace ForestOverlay.Data
{
    /// The window's colours: preset themes, the runner picks one in Settings
    /// -> Theme (T-0258; author, 2026-10-11: keep them all, plus the
    /// redesign's first look and Catppuccin's dark flavours). The first four follow the game's
    /// loading screen and the site (decisions.md *Look: yellow on black*).
    /// Each is a full set of the tokens UiKit draws with, as 0xRRGGBBAA.
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
            // The redesign's first look (UiKit before T-0258), kept as it was,
            // its own slightly warmer yellow included.
            new UiPalette
            {
                Id = "bluegrey", Name = "Blue-grey (first redesign)",
                Description = "The redesign's first look: blue-grey panels, a yellow accent.",
                Panel = 0x17191FFF, Card = 0x20232BFF, CardHover = 0x2A2E38FF, Border = 0x2E323CFF,
                ButtonBorder = 0x3D424FFF, ButtonHoverBorder = 0x575E6EFF, Field = 0x111217FF,
                Widget = 0x121317D6, WidgetBorder = 0x333842E6, Tip = 0x0A0B0EFA, TipBorder = 0x4D5463FF,
                Text = 0xE6E8EBFF, Dim = 0x99A1ABFF, Soft = 0xC7C7C7FF,
                Accent = 0xF5C417FF, AccentHover = 0xFFD64DFF, AccentDark = 0x6B540DFF, OnAccent = 0x0D0D0AFF,
                Warn = 0xFF8C33FF,
            },
            // Catppuccin (catppuccin.com, MIT): its dark flavours, mapped to
            // UiKit's tokens - mantle window, base cards, surfaces for edges,
            // subtext for hints, its yellow for the accent, peach for warnings.
            new UiPalette
            {
                Id = "catppuccin-mocha", Name = "Catppuccin Mocha",
                Description = "The darkest of Catppuccin's dark flavours: soft pastels on blue-violet, its yellow as the accent.",
                Panel = 0x181825FF, Card = 0x1E1E2EFF, CardHover = 0x313244FF, Border = 0x313244FF,
                ButtonBorder = 0x45475AFF, ButtonHoverBorder = 0x585B70FF, Field = 0x11111BFF,
                Widget = 0x11111BD6, WidgetBorder = 0x313244E6, Tip = 0x11111BFA, TipBorder = 0x585B70FF,
                Text = 0xCDD6F4FF, Dim = 0xA6ADC8FF, Soft = 0xBAC2DEFF,
                Accent = 0xF9E2AFFF, AccentHover = 0xFBECCBFF, AccentDark = 0x605955FF, OnAccent = 0x11111BFF,
                Warn = 0xFAB387FF,
            },
            new UiPalette
            {
                Id = "catppuccin-macchiato", Name = "Catppuccin Macchiato",
                Description = "The middle one of Catppuccin's dark flavours: soft pastels on blue-violet, its yellow as the accent.",
                Panel = 0x1E2030FF, Card = 0x24273AFF, CardHover = 0x363A4FFF, Border = 0x363A4FFF,
                ButtonBorder = 0x494D64FF, ButtonHoverBorder = 0x5B6078FF, Field = 0x181926FF,
                Widget = 0x181926D6, WidgetBorder = 0x363A4FE6, Tip = 0x181926FA, TipBorder = 0x5B6078FF,
                Text = 0xCAD3F5FF, Dim = 0xA5ADCBFF, Soft = 0xB8C0E0FF,
                Accent = 0xEED49FFF, AccentHover = 0xF4E3C1FF, AccentDark = 0x615B58FF, OnAccent = 0x181926FF,
                Warn = 0xF5A97FFF,
            },
            new UiPalette
            {
                Id = "catppuccin-frappe", Name = "Catppuccin Frappé",
                Description = "The lightest of Catppuccin's dark flavours: soft pastels on blue-violet, its yellow as the accent.",
                Panel = 0x292C3CFF, Card = 0x303446FF, CardHover = 0x414559FF, Border = 0x414559FF,
                ButtonBorder = 0x51576DFF, ButtonHoverBorder = 0x626880FF, Field = 0x232634FF,
                Widget = 0x232634D6, WidgetBorder = 0x414559E6, Tip = 0x232634FA, TipBorder = 0x626880FF,
                Text = 0xC6D0F5FF, Dim = 0xA5ADCEFF, Soft = 0xB5BFE2FF,
                Accent = 0xE5C890FF, AccentHover = 0xEEDBB7FF, AccentDark = 0x66605CFF, OnAccent = 0x232634FF,
                Warn = 0xEF9F76FF,
            },
        };

        /// Warm near-black: the author's pick (2026-10-11).
        public static UiPalette Default { get { return Find("warm"); } }

        /// The variant with this id; Default for an unknown or empty one.
        public static UiPalette Find(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return All[DefaultIndex];
        }

        private const int DefaultIndex = 2;   // warm

        public static float R(uint c) { return ((c >> 24) & 0xFF) / 255f; }
        public static float G(uint c) { return ((c >> 16) & 0xFF) / 255f; }
        public static float B(uint c) { return ((c >> 8) & 0xFF) / 255f; }
        public static float A(uint c) { return (c & 0xFF) / 255f; }
    }
}
