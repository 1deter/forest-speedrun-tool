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
        }

        [Fact]
        public void No_fault_gives_no_line_and_other_providers_are_ignored()
        {
            Assert.Null(GpuFault.Line(GpuFault.Parse("")));
            Assert.Null(GpuFault.Line(GpuFault.Parse(Real.Replace("nvlddmkm", "Disk"))));
        }
    }
}
