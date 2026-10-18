using ForestOverlay.BridgeMcp;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0395: the GPU driver fault line (tools/BridgeMcp), on a real event
    // from wevtutil (trimmed).
    public class GpuFaultTests
    {
        private const string Real =
            "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><Provider Name='nvlddmkm'/>" +
            "<EventID Qualifiers='0'>153</EventID><Level>2</Level>" +
            "<TimeCreated SystemTime='2026-10-16T08:26:33.2386473Z'/><EventRecordID>197090</EventRecordID>" +
            "<Channel>System</Channel></System><EventData><Data>\\Device\\Video3</Data>" +
            "<Data>Restarting TDR occurred on GPUID:100</Data><Binary>0000000002003000</Binary></EventData></Event>";

        [Fact]
        public void Real_event_is_parsed()
        {
            var faults = GpuFault.Parse(Real + Real);
            Assert.Equal(2, faults.Count);
            Assert.Equal(153, faults[0].EventId);
            Assert.Equal("Restarting TDR occurred on GPUID:100", faults[0].Text);
            Assert.Equal(2026, faults[0].Utc.Year);
        }

        [Fact]
        public void Line_names_the_fault_and_says_close_the_game()
        {
            string line = GpuFault.Line(GpuFault.Parse(Real));
            Assert.Contains("nvlddmkm", line);
            Assert.Contains("Restarting TDR occurred", line);
            Assert.Contains("close the game", line);
            Assert.Contains("event 153", line);
        }

        [Fact]
        public void Many_events_pick_the_newest_first_and_cap_the_count()
        {
            string newest = Real.Replace("153", "117").Replace("2026-10-16T08:26:33", "2026-10-16T09:00:00");
            string xml = newest;
            for (int i = 0; i < 25; i++) xml += Real;
            Assert.Contains("20+", GpuFault.Line(GpuFault.Parse(Rep(21))));
            Assert.DoesNotContain("+", GpuFault.Line(GpuFault.Parse(Rep(20))));
            var faults = GpuFault.Parse(xml);
            Assert.Equal(26, faults.Count);
            string line = GpuFault.Line(faults);
            Assert.Contains("event 117", line);
            Assert.Contains("20+", line);
            Assert.Contains("3 nvlddmkm", GpuFault.Line(GpuFault.Parse(Real + Real + Real)));
        }

        [Fact]
        public void No_fault_gives_no_line_and_other_providers_are_ignored()
        {
            Assert.Null(GpuFault.Line(GpuFault.Parse("")));
            Assert.Null(GpuFault.Line(GpuFault.Parse(Real.Replace("nvlddmkm", "Disk"))));
        }

        private static string Rep(int n)
        {
            string x = "";
            for (int i = 0; i < n; i++) x += Real;
            return x;
        }

        [Fact]
        public void Check_reads_21_and_notes_a_read_failure()
        {
            string asked = null;
            string line = GpuFault.Check(System.DateTime.UtcNow, a => { asked = a; return Rep(1); });
            Assert.Contains("/c:21", asked);
            Assert.Contains("/rd:true", asked);
            Assert.Contains("event 153", line);
            Assert.Null(GpuFault.Check(System.DateTime.UtcNow, a => ""));
            string t = GpuFault.Check(System.DateTime.UtcNow, a => throw new System.TimeoutException());
            Assert.Contains("could not read the System log (timed out)", t);
            string e = GpuFault.Check(System.DateTime.UtcNow, a => throw new System.InvalidOperationException("no wevtutil"));
            Assert.Contains("no wevtutil", e);
        }
    }
}
