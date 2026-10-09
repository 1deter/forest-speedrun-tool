using System;
using System.Collections.Generic;
using System.Reflection;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // Stand-ins for PlayMaker's types: DumpText reaches them by type and
    // member name only, as it does in the game.
    public class FsmEvent { public string Name = "OnHit"; }
    public class FsmFloat
    {
        public string Name { get; set; }
        public bool UseVariable { get; set; }
        public bool IsNone { get; set; }
        public float Value { get; set; }
    }
    public class FsmOwnerDefault { public string OwnerOption = "UseOwner"; public object GameObject; }
    public class FsmEventTarget { public string target = "Self"; public object gameObject; public object fsmName; public object sendToChildren; }
    public class FsmProperty { public string PropertyName = "isGrounded"; public object TargetObject; }
    public class FunctionCall { public string FunctionName = "Hit"; public string ParameterType = "int"; }
    public struct Stamp { public override string ToString() { return "a\nb"; } }
    public enum Side { Left, Right }

    public class DumpTextTests
    {
        private static string V(object v) { return DumpText.FsmValue(v, 0, null); }

        [Fact]
        public void OneLine_FoldsBreaksAndCutsAt200()
        {
            Assert.Equal("a  b c", DumpText.OneLine("a\r\nb\nc"));
            string longText = new string('x', 250);
            Assert.Equal(new string('x', 200) + "...", DumpText.OneLine(longText));
            Assert.Equal(new string('x', 200), DumpText.OneLine(new string('x', 200)));
        }

        [Fact]
        public void Clean_KeepsRecordsOnOneLineAndListsSplittable()
        {
            Assert.Equal("-", DumpText.Clean(null));
            Assert.Equal("-", DumpText.Clean(""));
            Assert.Equal("a b c,d", DumpText.Clean("a\tb\nc;d"));
        }

        [Fact]
        public void Num_IsInvariantWithUpToFiveDecimals()
        {
            Assert.Equal("1.5", DumpText.Num(1.5f));
            Assert.Equal("0.33333", DumpText.Num(1f / 3f));
            Assert.Equal("-2", DumpText.Num(-2f));
        }

        [Fact]
        public void FilenameSafe_ReplacesAndCaps()
        {
            Assert.Equal("all", DumpText.FilenameSafe(null));
            Assert.Equal("TheForest_Player_Stats", DumpText.FilenameSafe("TheForest.Player Stats"));
            Assert.Equal("a_b", DumpText.FilenameSafe("a/b"));
            Assert.Equal(40, DumpText.FilenameSafe(new string('a', 60)).Length);
        }

        private static void Sample(int count, string label) { }
        private static void NoArgs() { }

        [Fact]
        public void ParamList_NamesTypesAndParameters()
        {
            MethodInfo m = typeof(DumpTextTests).GetMethod("Sample", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.Equal("Int32 count, String label", DumpText.ParamList(m));
            Assert.Equal("", DumpText.ParamList(typeof(DumpTextTests).GetMethod("NoArgs", BindingFlags.NonPublic | BindingFlags.Static)));
            Assert.Equal("?", DumpText.TypeName(null));
        }

        [Fact]
        public void Member_ReadsPropertiesAndPrivateFields_NullWhenMissing()
        {
            Assert.Equal("OnHit", DumpText.Member(new FsmEvent(), "Name"));
            Assert.Equal(3, DumpText.Member(new List<int> { 1, 2, 3 }, "Count"));
            Assert.Null(DumpText.Member(new FsmEvent(), "Nope"));
            Assert.Null(DumpText.Member(null, "Name"));
        }

        [Fact]
        public void FsmValue_Primitives()
        {
            Assert.Equal("null", V(null));
            Assert.Equal("\"a b\"", V("a\nb"));
            Assert.Equal("0.333", V(1f / 3f));
            Assert.Equal("True", V(true));
            Assert.Equal("7", V(7));
            Assert.Equal("Right", V(Side.Right));
        }

        [Fact]
        public void FsmValue_VariableReferenceReadsItsName()
        {
            Assert.Equal("{speed}", V(new FsmFloat { Name = "speed", UseVariable = true }));
            Assert.Equal("none", V(new FsmFloat { IsNone = true }));
            Assert.Equal("2.5", V(new FsmFloat { Value = 2.5f }));
            // Deep inside a list, only the type name.
            Assert.Equal("FsmFloat", DumpText.FsmValue(new FsmFloat { Value = 1f }, 2, null));
        }

        [Fact]
        public void FsmValue_PlayMakerShapes()
        {
            Assert.Equal("event OnHit", V(new FsmEvent()));
            Assert.Equal("owner", V(new FsmOwnerDefault()));
            Assert.Equal("gameobject \"rock\"", V(new FsmOwnerDefault { OwnerOption = "SpecifyGameObject", GameObject = "rock" }));
            Assert.Equal("to Self", V(new FsmEventTarget()));
            Assert.Equal("to GameObject \"rock\" fsm \"brain\" (+children)",
                V(new FsmEventTarget { target = "GameObject", gameObject = "rock", fsmName = "brain", sendToChildren = true }));
            Assert.Equal("to GameObject null", V(new FsmEventTarget { target = "GameObject", fsmName = "" }));
            Assert.Equal("property isGrounded of null", V(new FsmProperty()));
            Assert.Equal("call Hit (int)", V(new FunctionCall()));
        }

        [Fact]
        public void FsmValue_ListsCapAt24AndStopNesting()
        {
            List<int> many = new List<int>();
            for (int i = 0; i < 30; i++) many.Add(i);
            string s = V(many);
            Assert.StartsWith("[0, 1, 2", s);
            Assert.EndsWith("22, 23, ...]", s);

            Assert.Equal("[[1, 2], [3]]", V(new List<List<int>> { new List<int> { 1, 2 }, new List<int> { 3 } }));
            // A list at depth 2 is not expanded: a reference type reads its type name.
            Assert.Equal("List`1", DumpText.FsmValue(new List<int> { 1 }, 2, null));
        }

        [Fact]
        public void FsmValue_StructsOnOneLineOtherObjectsByTypeName()
        {
            Assert.Equal("a b", V(new Stamp()));
            Assert.Equal("Object", V(new object()));
        }

        [Fact]
        public void FsmValue_EngineHookComesBeforeThePlayMakerShapes()
        {
            Func<object, string> engine = v => v is FsmEvent ? "engine" : null;
            Assert.Equal("engine", DumpText.FsmValue(new FsmEvent(), 0, engine));
            Assert.Equal("[engine, 1]", DumpText.FsmValue(new object[] { new FsmEvent(), 1 }, 0, engine));
            // Primitives never reach the hook.
            Assert.Equal("1", DumpText.FsmValue(1, 0, v => "engine"));
        }
    }
}
