using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // forest-bridge-mcp: an MCP server (stdio) over ForestOverlay's live
    // test bridge. See BridgeMcp.csproj and CLAUDE.md (The live test bridge).
    //
    //   forest-bridge-mcp            serve MCP on stdin / stdout
    //   forest-bridge-mcp --tools    print the tool list (a quick check)
    //   forest-bridge-mcp --call <tool> [json-args]
    //                                run one tool, print its result as JSON
    //                                (scripts/e2e.py); exit 1 on an error
    internal static class Program
    {
        public const string Version = "1.0.0";

        private static async Task<int> Main(string[] args)
        {
            ForestPaths paths = new ForestPaths();
            BridgeClient bridge = new BridgeClient(paths);
            Tools tools = new Tools(paths, bridge);
            new DiscordTools().Register(tools.All);

            if (args.Length > 0 && args[0] == "--tools")
            {
                foreach (Tool t in tools.All) Console.WriteLine(t.Name + " - " + t.Description);
                Console.WriteLine();
                Console.WriteLine("game folder " + paths.Root + ", bridge folder " + paths.Bridge);
                return 0;
            }

            if (args.Length > 0 && args[0] == "--call")
            {
                Tool tool = args.Length > 1 ? tools.All.Find(t => t.Name == args[1]) : null;
                if (tool == null) { Console.Error.WriteLine("usage: --call <tool> [json-args]; --tools lists them"); return 2; }
                JsonObject arguments = args.Length > 2 ? JsonNode.Parse(args[2]) as JsonObject : new JsonObject();
                ToolResult result = await McpServer.Invoke(tool, arguments, CancellationToken.None);
                Console.OutputEncoding = new UTF8Encoding(false);
                Console.WriteLine(result.ToJson().ToJsonString());
                return result.IsError ? 1 : 0;
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
