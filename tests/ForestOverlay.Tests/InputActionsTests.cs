using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class InputActionsTests
    {
        private static InputActionTable Defaults()
        {
            InputActionTable t = new InputActionTable();
            int vert = t.AddAction("Vertical", 6), horiz = t.AddAction("Horizontal", 6);
            int crouch = t.AddAction("Crouch", 6), batch = t.AddAction("Batch", 4);
            int take = t.AddAction("Take", 2), build = t.AddAction("Build", 3);
            int fire = t.AddAction("Fire1", 2), equip = t.AddAction("Equip", 4);
            int jump = t.AddAction("Jump", 6), menuJump = t.AddAction("MenuSelect", 1);
            t.AddBinding("W", vert, 1);
            t.AddBinding("S", vert, -1);
            t.AddBinding("D", horiz, 1);
            t.AddBinding("A", horiz, -1);
            t.AddBinding("LeftControl", batch, 1);   // map order: the lower-ranked one first
            t.AddBinding("LeftControl", crouch, 1);
            t.AddBinding("E", build, 1);
            t.AddBinding("E", take, 1);
            t.AddBinding("Mouse0", equip, 1);
            t.AddBinding("Mouse0", fire, 1);
            t.AddBinding("Space", menuJump, 1);
            t.AddBinding("Space", jump, 1);
            t.AddBinding("Return", menuJump, 1);
            return t;
        }

        private static string[] Names(InputActionTable t, string key, out int[] signs)
        {
            int[] a = new int[InputActionTable.MaxPerKey];
            signs = new int[a.Length];
            int n = t.Resolve(key, a, signs);
            string[] o = new string[n];
            for (int i = 0; i < n; i++) o[i] = t.NameOf(a[i]);
            return o;
        }

        [Fact]
        public void AxisKeys_CarryTheirSign()
        {
            InputActionTable t = Defaults();
            int[] s;
            Assert.Equal(new[] { "Vertical" }, Names(t, "W", out s));
            Assert.Equal(1, s[0]);
            Names(t, "S", out s);
            Assert.Equal(-1, s[0]);
            Names(t, "A", out s);
            Assert.Equal(-1, s[0]);
        }

        [Fact]
        public void TwoActionKeys_ListedInPickOrder()
        {
            InputActionTable t = Defaults();
            int[] s;
            Assert.Equal(new[] { "Crouch", "Batch" }, Names(t, "LeftControl", out s));
            Assert.Equal(new[] { "Take", "Build" }, Names(t, "E", out s));
            Assert.Equal(new[] { "Fire1", "Equip" }, Names(t, "Mouse0", out s));
        }

        [Fact]
        public void MenuActions_OnlyWhenNothingElse()
        {
            InputActionTable t = Defaults();
            int[] s;
            Assert.Equal(new[] { "Jump" }, Names(t, "Space", out s));
            Assert.Equal(new[] { "MenuSelect" }, Names(t, "Return", out s));
        }

        [Fact]
        public void UnboundKey_HasNoAction()
        {
            int[] s;
            Assert.Empty(Names(Defaults(), "F7", out s));
        }

        [Fact]
        public void DuplicateBinding_IsKeptOnce()
        {
            InputActionTable t = Defaults();
            int before = t.RowCount;
            t.AddBinding("W", t.IndexOf("Vertical"), 1);
            Assert.Equal(before, t.RowCount);
        }

        [Fact]
        public void Labels()
        {
            Assert.Equal("Forward", InputActionTable.Label("Vertical", 1));
            Assert.Equal("Back", InputActionTable.Label("Vertical", -1));
            Assert.Equal("Left", InputActionTable.Label("Horizontal", -1));
            Assert.Equal("Right", InputActionTable.Label("Horizontal", 1));
            Assert.Equal("Attack", InputActionTable.Label("Fire1", 1));
            Assert.Equal("Crouch", InputActionTable.Label("Crouch", 1));
            Assert.Equal("Menu", InputActionTable.Label("Esc", 1));
        }

        [Fact]
        public void MoreThanMaxPerKey_IsCountedNotLost()
        {
            InputActionTable t = new InputActionTable();
            for (int i = 0; i < InputActionTable.MaxPerKey + 2; i++) t.AddBinding("F", t.AddAction("A" + i, 2), 1);
            int[] a = new int[InputActionTable.MaxPerKey], s = new int[InputActionTable.MaxPerKey];
            Assert.Equal(InputActionTable.MaxPerKey, t.Resolve("F", a, s));
            Assert.Equal(InputActionTable.MaxPerKey + 2, t.Total("F"));   // the module logs the difference
            Assert.Equal(0, t.Total("G"));
        }
    }
}
