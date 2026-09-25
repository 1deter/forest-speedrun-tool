using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // forest-bridge-mcp: an MCP server (stdio) over ForestOverlay's live
    // test bridge. See BridgeMcp.csproj and CLAUDE.md (The live test bridge).
    //
    //   forest-bridge-mcp            serve MCP on stdin / stdout
    //   forest-bridge-mcp --tools    print the tool list (a quick check)
    internal static class Program
    {
        public const string Version = "1.0.0";

        private static async Task<int> Main(string[] args)
        {
            ForestPaths paths = new ForestPaths();
            BridgeClient bridge = new BridgeClient(paths);
            Tools tools = new Tools(paths, bridge);

            if (args.Length > 0 && args[0] == "--tools")
            {
                foreach (Tool t in tools.All) Console.WriteLine(t.Name + " - " + t.Description);
                Console.WriteLine();
                Console.WriteLine("game folder " + paths.Root + ", bridge folder " + paths.Bridge);
                return 0;
            }

            if (args.Length > 0 && args[0] == "--windows")
            {
                // What the launcher looks like to Launcher.ClickPlay.
                using System.Diagnostics.Process p = GameProcess.Find();
                if (p == null) { Console.WriteLine("TheForest.exe is not running"); return 1; }
                foreach (Launcher.Window w in Launcher.WindowsOf(p.Id))
                {
                    Console.WriteLine("'" + w.Title + "' [" + w.Class + "]");
                    foreach (var c in w.Children) Console.WriteLine("    [" + c.cls + "] '" + c.text + "'");
                }
                return 0;
            }

            // Protocol only on stdout, UTF-8 without a BOM.
            UTF8Encoding utf8 = new UTF8Encoding(false);
            TextWriter output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
            TextReader input = new StreamReader(Console.OpenStandardInput(), utf8);
            Console.SetOut(Console.Error);   // a stray Console.WriteLine must not corrupt the stream

            Console.Error.WriteLine("forest-bridge-mcp " + Version + ": game folder " + paths.Root + ", bridge " + paths.Bridge);
            McpServer server = new McpServer(tools.All, output, Tools.Instructions);
            await server.RunAsync(input);
            return 0;
        }
    }
}
