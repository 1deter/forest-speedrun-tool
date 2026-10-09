namespace ForestOverlay.ILScan.Tests
{
    // Known IL for the scanner to find. The names are unusual on purpose:
    // a needle matches nothing else in this assembly or its references.
    public class ScanFixture
    {
        public int zzHealth;
        public static int zzCount;
        public string ZzLabel { get; set; }

        public void Hurt() { zzHealth = zzHealth - 1; }
        public void Tally() { zzCount++; }
        public int Peek() { return zzHealth; }
        public void Rename() { ZzLabel = "renamed"; }
        public void Shout() { Send("OnZzFixtureMessage"); }

        private static void Send(string message) { }

        public class Nested
        {
            public void Poke(ScanFixture f) { f.zzHealth = 0; }
        }
    }
}
