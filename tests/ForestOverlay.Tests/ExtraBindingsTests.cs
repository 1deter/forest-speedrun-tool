using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class ExtraBindingsTests
    {
        // A few KeyCode names, as the game side's Enum check would accept them.
        private static readonly string[] Keys = { "J", "K", "Space", "LeftShift", "F5" };

        private static string KeyName(string s)
        {
            foreach (string k in Keys)
                if (string.Equals(k, s, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        private static List<ExtraBinding> P(string text, List<string> skipped = null)
        {
            return ExtraBindings.Parse(text, KeyName, skipped);
        }

        [Fact]
        public void Parse_Format_RoundTrip()
        {
            const string text = "Jump=WheelUp, Crouch=Mouse3, Vertical-=K";
            List<ExtraBinding> list = P(text);
            Assert.Equal(3, list.Count);
            Assert.Equal(text, ExtraBindings.Format(list));
            Assert.Equal(text, ExtraBindings.Format(P(ExtraBindings.Format(list))));
        }

        [Fact]
        public void MouseButtons_AreElements3To9()
        {
            for (int i = 0; i <= 6; i++)
            {
                ExtraBinding b = P("Jump=Mouse" + i)[0];
                Assert.Equal(BindInput.MouseButton, b.Kind);
                Assert.Equal(3 + i, b.Element);
                Assert.Equal("Mouse" + i, b.Input);
                Assert.True(b.IsMouse);
            }
            Assert.Equal("Mouse3", P("Jump=mouse3")[0].Input);   // any case, saved canonical
        }

        [Fact]
        public void WheelTokens_AreElement2_UpAndDown()
        {
            ExtraBinding up = P("Jump=WheelUp")[0];
            ExtraBinding down = P("Jump=wheeldown")[0];
            Assert.Equal(BindInput.Wheel, up.Kind);
            Assert.Equal(2, up.Element);
            Assert.False(up.WheelDown);
            Assert.Equal(2, down.Element);
            Assert.True(down.WheelDown);
            Assert.Equal("WheelDown", down.Input);
        }

        [Fact]
        public void Keys_AreCanonical_AndHaveNoElement()
        {
            ExtraBinding b = P("Jump=leftshift")[0];
            Assert.Equal(BindInput.Key, b.Kind);
            Assert.Equal("LeftShift", b.Input);
            Assert.Equal(-1, b.Element);
            Assert.False(b.IsMouse);
        }

        [Fact]
        public void AxisSuffix_MinusIsNegative_PlusIsDefault()
        {
            List<ExtraBinding> list = P("Vertical-=K, Vertical+=J, Mouse X=Space");
            Assert.True(list[0].Negative);
            Assert.Equal("Vertical", list[0].Action);
            Assert.False(list[1].Negative);
            Assert.Equal("Vertical", list[1].Action);
            Assert.Equal("Mouse X", list[2].Action);   // a name with a space
            Assert.Equal("Vertical-=K, Vertical=J, Mouse X=Space", ExtraBindings.Format(list));
        }

        [Fact]
        public void Duplicates_AreDropped_SidesAreDifferent()
        {
            List<ExtraBinding> list = P("Jump=J, jump=J, Jump+=j, Jump-=J, Jump=K");
            Assert.Equal("Jump=J, Jump-=J, Jump=K", ExtraBindings.Format(list));
            Assert.False(ExtraBindings.AddUnique(list, ExtraBindings.Make("Jump", false, "J", KeyName)));
            Assert.True(ExtraBindings.AddUnique(list, ExtraBindings.Make("Jump", false, "WheelUp", KeyName)));
            Assert.Equal(4, list.Count);
        }

        [Fact]
        public void Garbage_IsSkippedAndReported_NeverThrows()
        {
            List<string> skipped = new List<string>();
            List<ExtraBinding> list = P("Jump=J, nonsense, =K, Jump=, Jump=NotAKey, Jump=Mouse7, Jump=Mouse, ,,  Crouch=Mouse3 ", skipped);
            Assert.Equal("Jump=J, Crouch=Mouse3", ExtraBindings.Format(list));
            Assert.Equal(new[] { "nonsense", "=K", "Jump=", "Jump=NotAKey", "Jump=Mouse7", "Jump=Mouse" }, skipped.ToArray());
            Assert.Empty(P("-=J"));   // a sign with no action
        }

        [Fact]
        public void Empty_IsAnEmptyList()
        {
            Assert.Empty(P(""));
            Assert.Empty(P(null));
            Assert.Empty(P("  ,  "));
            Assert.Equal("", ExtraBindings.Format(new List<ExtraBinding>()));
            Assert.Equal("", ExtraBindings.Format(null));
        }

        [Fact]
        public void InputLabel_NamesMouseAndWheel()
        {
            Assert.Equal("Mouse wheel up", ExtraBindings.InputLabel("WheelUp"));
            Assert.Equal("Mouse wheel down", ExtraBindings.InputLabel("WheelDown"));
            Assert.Equal("Left mouse", ExtraBindings.InputLabel("Mouse0"));
            Assert.Equal("Mouse 4", ExtraBindings.InputLabel("Mouse3"));
            Assert.Equal("J", ExtraBindings.InputLabel("J"));
        }

        // T-0462: one input never drives several actions.
        [Fact]
        public void Clashes_OnlyAnotherActionOrSideOnTheSameInput()
        {
            ExtraBinding jumpK = P("Jump=K")[0];
            Assert.True(ExtraBindings.Clashes(P("Crouch=K")[0], jumpK));
            Assert.True(ExtraBindings.Clashes(P("Vertical-=K")[0], P("Vertical=K")[0]));
            Assert.False(ExtraBindings.Clashes(P("jump=K")[0], jumpK));        // the same binding: not a clash
            Assert.False(ExtraBindings.Clashes(P("Crouch=J")[0], jumpK));      // another input
            Assert.False(ExtraBindings.Clashes(P("Jump=WheelUp")[0], P("Crouch=WheelDown")[0]));   // the wheel's two directions are two inputs
            Assert.True(ExtraBindings.Clashes(P("Jump=WheelUp")[0], P("Crouch=wheelup")[0]));
            Assert.False(ExtraBindings.Clashes(null, jumpK));
        }

        [Fact]
        public void ClashIn_FindsTheBindingAlreadyOnTheInput()
        {
            List<ExtraBinding> list = P("Jump=WheelUp, Crouch=Mouse3");
            Assert.Same(list[1], ExtraBindings.ClashIn(list, P("Sprint=Mouse3")[0]));
            Assert.Null(ExtraBindings.ClashIn(list, P("Sprint=Mouse4")[0]));
            Assert.Null(ExtraBindings.ClashIn(list, P("Crouch=Mouse3")[0]));
            Assert.Null(ExtraBindings.ClashIn(null, P("Crouch=Mouse3")[0]));
        }

        [Fact]
        public void DropClashes_TheFirstSavedKeepsItsInput()
        {
            List<ExtraBinding> list = P("Jump=K, Crouch=K, Sprint=J, Vertical-=J, Use=K, Jump=Mouse3");
            List<ExtraBinding> dropped = new List<ExtraBinding>();
            Assert.Equal(3, ExtraBindings.DropClashes(list, dropped));
            Assert.Equal("Jump=K, Sprint=J, Jump=Mouse3", ExtraBindings.Format(list));
            Assert.Equal("Crouch=K, Vertical-=J, Use=K", ExtraBindings.Format(dropped));

            List<ExtraBinding> clean = P("Jump=K, Crouch=J");
            Assert.Equal(0, ExtraBindings.DropClashes(clean, null));
            Assert.Equal(2, clean.Count);
        }
    }
}
