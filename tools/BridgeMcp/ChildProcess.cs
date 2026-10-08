using System.Diagnostics;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // Starting a helper process from a tool call (gotcha 95, T-0133). The
    // server's own stdin is the MCP pipe, which never closes: a child that
    // inherits it and reads (or runs something that does - a git under
    // tasks.py did, T-0007) waits forever and the tool reports "cancelled".
    // Every child therefore gets a stdin of its own, closed at once.
    // ------------------------------------------------------------------
    internal static class ChildProcess
    {
        /// Starts `psi` with its stdin redirected and closed (UseShellExecute
        /// is forced off - a redirect needs it).
        public static Process Start(ProcessStartInfo psi)
        {
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            Process p = Process.Start(psi);
            if (p != null) p.StandardInput.Close();
            return p;
        }
    }
}
