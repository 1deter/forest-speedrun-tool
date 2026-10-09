using System;
using System.IO;
using Xunit;

namespace ForestOverlay.ILScan.Tests
{
    // ilscan's Main over this assembly (Fixture.cs). One class: Console is
    // redirected, and xunit runs one class's tests one at a time.
    public class ScanTests
    {
        private static readonly string Asm = typeof(ScanFixture).Assembly.Location;
        private const string Fx = "ForestOverlay.ILScan.Tests.ScanFixture";

        private static int Run(out string stdout, out string stderr, params string[] args)
        {
            TextWriter oldOut = Console.Out, oldErr = Console.Error;
            StringWriter o = new StringWriter(), e = new StringWriter();
            Console.SetOut(o);
            Console.SetError(e);
            try { return Program.Main(args); }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldErr);
                stdout = o.ToString().Replace("\r\n", "\n");
                stderr = e.ToString();
            }
        }

        private static string Scan(params string[] args)
        {
            string[] full = new string[args.Length + 2];
            args.CopyTo(full, 0);
            full[args.Length] = "--asm";
            full[args.Length + 1] = Asm;
            Assert.Equal(0, Run(out string stdout, out string stderr, full));
            Assert.Equal("", stderr);
            return stdout;
        }

        [Fact]
        public void Refs_ListsEveryMethodTouchingTheMember()
        {
            string s = Scan("refs", "zzHealth");
            Assert.Contains(Fx + "::Hurt\n", s);
            Assert.Contains(Fx + "::Peek\n", s);
            Assert.Contains(Fx + "/Nested::Poke\n", s);
            Assert.Contains("    ldfld " + Fx + "::zzHealth\n", s);
            Assert.Contains("# 3 match(es)\n", s);
        }

        [Fact]
        public void Writes_ListsOnlyStores()
        {
            string s = Scan("writes", "zzHealth");
            Assert.Contains(Fx + "::Hurt\n    STORE stfld " + Fx + "::zzHealth\n", s);
            Assert.Contains(Fx + "/Nested::Poke\n", s);
            Assert.DoesNotContain("::Peek", s);
            Assert.Contains("# 2 match(es)\n", s);
        }

        [Fact]
        public void Writes_SeesStaticFieldsAndPropertySetters()
        {
            Assert.Contains(Fx + "::Tally\n    STORE stsfld " + Fx + "::zzCount\n", Scan("writes", "zzCount"));
            string s = Scan("writes", "ZzLabel");
            Assert.Contains(Fx + "::Rename\n    STORE call " + Fx + "::set_ZzLabel\n", s);
        }

        [Fact]
        public void Strings_FindsTheLiteralAndTheCallThatTakesIt()
        {
            string s = Scan("strings", "OnZzFixtureMessage");
            Assert.Contains(Fx + "::Shout\n    ldstr \"OnZzFixtureMessage\"  -> " + Fx + "::Send\n", s);
        }

        [Fact]
        public void Body_DisassemblesTheMethod()
        {
            string s = Scan("body", "ScanFixture::Hurt");
            Assert.Contains("=== " + Fx + "::Hurt ===\n", s);
            Assert.Contains("stfld System.Int32 " + Fx + "::zzHealth", s);
            Assert.Contains("# 1 match(es)\n", s);
        }

        [Fact]
        public void Type_ListsMembersOfMatchingAndNestedTypes()
        {
            string s = Scan("type", "ScanFixture");
            Assert.Contains("=== " + Fx + "  :  System.Object ===\n", s);
            Assert.Contains("    field  Int32 zzHealth\n", s);
            Assert.Contains("    field  Int32 zzCount  [static]\n", s);
            Assert.Contains("    prop   String ZzLabel\n", s);
            Assert.Contains("    method Void Poke(ScanFixture f)\n", s);
            Assert.Contains("=== " + Fx + "/Nested  :  System.Object ===\n", s);
        }

        [Fact]
        public void Max_CapsTheMatches()
        {
            string s = Scan("refs", "zzHealth", "--max", "1");
            Assert.Contains("# 1 match(es) (capped at 1)\n", s);
            Assert.Single(s.Split('\n'), l => l.StartsWith(Fx));
        }

        [Fact]
        public void MatchingIsCaseInsensitive()
        {
            Assert.Contains(Fx + "::Hurt\n", Scan("refs", "ZZHEALTH"));
        }

        [Fact]
        public void BadArguments_ExitWithTheirCodes()
        {
            Assert.Equal(2, Run(out _, out string usage, "refs"));
            Assert.StartsWith("usage: ilscan", usage);
            Assert.Equal(2, Run(out _, out string mode, "nope", "x", "--asm", Asm));
            Assert.Contains("unknown mode: nope", mode);
            Assert.Equal(3, Run(out _, out string missing, "refs", "x", "--asm", Path.Combine(Path.GetTempPath(), "no-such-ilscan.dll")));
            Assert.Contains("Assembly-CSharp.dll not found", missing);
        }
    }
}
