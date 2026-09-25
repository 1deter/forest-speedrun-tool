using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // The MCP side: newline-delimited JSON-RPC 2.0 over stdin / stdout
    // (the stdio transport). Only what a tool server needs: initialize,
    // ping, tools/list, tools/call, cancellation. Tool calls run on the
    // thread pool so a long one (a Full load, a game restart) does not
    // hold up pings; the bridge itself takes one batch at a time.
    // Nothing but protocol goes to stdout - diagnostics go to stderr.
    // ------------------------------------------------------------------
    internal sealed class Tool
    {
        public string Name;
        public string Description;
        public JsonObject Schema;
        public Func<Args, CancellationToken, Task<ToolResult>> Run;
    }

    internal sealed class ToolResult
    {
        private readonly JsonArray _content = new JsonArray();
        public bool IsError;

        public static ToolResult Text(string text, bool error = false)
        {
            ToolResult r = new ToolResult { IsError = error };
            return r.AddText(text);
        }

        public static ToolResult Fail(string text) { return Text(text, true); }

        public ToolResult AddText(string text)
        {
            _content.Add(new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrEmpty(text) ? "(no output)" : text });
            return this;
        }

        public ToolResult AddImage(byte[] data, string mime)
        {
            _content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(data), ["mimeType"] = mime });
            return this;
        }

        public JsonObject ToJson()
        {
            return new JsonObject { ["content"] = _content, ["isError"] = IsError };
        }
    }

    internal sealed class McpServer
    {
        private static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly List<Tool> _tools;
        private readonly Dictionary<string, Tool> _byName = new Dictionary<string, Tool>(StringComparer.Ordinal);
        private readonly TextWriter _out;
        private readonly object _writeLock = new object();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _running =
            new ConcurrentDictionary<string, CancellationTokenSource>();
        private readonly string _instructions;

        public McpServer(List<Tool> tools, TextWriter output, string instructions)
        {
            _tools = tools;
            foreach (Tool t in tools) _byName[t.Name] = t;
            _out = output;
            _instructions = instructions;
        }

        public async Task RunAsync(TextReader input)
        {
            string line;
            while ((line = await input.ReadLineAsync()) != null)
            {
                if (line.Trim().Length == 0) continue;
                JsonNode msg;
                try { msg = JsonNode.Parse(line); }
                catch (Exception ex)
                {
                    Send(Error(null, -32700, "parse error: " + ex.Message));
                    continue;
                }
                if (msg is JsonArray batch)
                {
                    foreach (JsonNode m in batch) Dispatch(m as JsonObject);
                }
                else Dispatch(msg as JsonObject);
            }
            // stdin closed: the client is gone. Withdraw what is waiting.
            foreach (CancellationTokenSource cts in _running.Values) cts.Cancel();
        }

        private void Dispatch(JsonObject m)
        {
            if (m == null) return;
            string method = (string)m["method"];
            JsonNode id = m["id"];
            JsonObject p = m["params"] as JsonObject ?? new JsonObject();
            if (method == null) return;   // a response - this server sends no requests

            if (id == null)
            {
                if (method == "notifications/cancelled")
                {
                    JsonNode rid = p["requestId"];
                    CancellationTokenSource cts;
                    if (rid != null && _running.TryGetValue(rid.ToJsonString(), out cts)) cts.Cancel();
                }
                return;
            }

            switch (method)
            {
                case "initialize": Reply(id, Initialize(p)); break;
                case "ping": Reply(id, new JsonObject()); break;
                case "tools/list": Reply(id, ListTools()); break;
                case "tools/call":
                {
                    JsonNode idCopy = id.DeepClone();
                    Task.Run(() => CallTool(idCopy, p));
                    break;
                }
                case "resources/list": Reply(id, new JsonObject { ["resources"] = new JsonArray() }); break;
                case "resources/templates/list": Reply(id, new JsonObject { ["resourceTemplates"] = new JsonArray() }); break;
                case "prompts/list": Reply(id, new JsonObject { ["prompts"] = new JsonArray() }); break;
                default: Send(Error(id, -32601, "method not found: " + method)); break;
            }
        }

        private JsonObject Initialize(JsonObject p)
        {
            string asked = (string)p["protocolVersion"];
            string version = Array.IndexOf(Versions, asked) >= 0 ? asked : Versions[0];
            return new JsonObject
            {
                ["protocolVersion"] = version,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "forest-bridge", ["version"] = Program.Version },
                ["instructions"] = _instructions,
            };
        }

        private JsonObject ListTools()
        {
            JsonArray list = new JsonArray();
            foreach (Tool t in _tools)
                list.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["inputSchema"] = t.Schema.DeepClone(),
                });
            return new JsonObject { ["tools"] = list };
        }

        private async Task CallTool(JsonNode id, JsonObject p)
        {
            string key = id.ToJsonString();
            string name = (string)p["name"];
            Tool tool;
            if (name == null || !_byName.TryGetValue(name, out tool))
            {
                Reply(id, ToolResult.Fail("unknown tool '" + name + "'").ToJson());
                return;
            }

            CancellationTokenSource cts = new CancellationTokenSource();
            _running[key] = cts;
            ToolResult result;
            DateTime start = DateTime.UtcNow;
            try
            {
                result = await tool.Run(new Args(p["arguments"] as JsonObject), cts.Token);
            }
            catch (OperationCanceledException)
            {
                result = ToolResult.Fail("cancelled");
            }
            catch (ArgumentException ex)
            {
                result = ToolResult.Fail(ex.Message);
            }
            catch (Exception ex)
            {
                result = ToolResult.Fail(name + " threw " + ex.GetType().Name + ": " + ex.Message);
                Console.Error.WriteLine(ex);
            }
            finally
            {
                _running.TryRemove(key, out _);
                cts.Dispose();
            }
            Console.Error.WriteLine("tool " + name + " " + (result.IsError ? "error" : "ok") + " in " +
                                    (DateTime.UtcNow - start).TotalSeconds.ToString("0.0") + " s");
            Reply(id, result.ToJson());
        }

        private void Reply(JsonNode id, JsonNode result)
        {
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result });
        }

        private static JsonObject Error(JsonNode id, int code, string message)
        {
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id == null ? null : id.DeepClone(),
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
            };
        }

        private void Send(JsonObject msg)
        {
            string text = msg.ToJsonString();
            lock (_writeLock)
            {
                _out.Write(text);
                _out.Write('\n');
                _out.Flush();
            }
        }
    }

    /// Tool arguments with typed, forgiving reads (a number sent as a
    /// string still reads as a number). Bad input throws ArgumentException,
    /// which becomes the tool's error text.
    internal sealed class Args
    {
        private readonly JsonObject _o;

        public Args(JsonObject o) { _o = o ?? new JsonObject(); }

        public bool Has(string name) { return _o[name] != null; }

        public string Str(string name, string fallback = null)
        {
            JsonNode n = _o[name];
            if (n == null) return fallback;
            if (n is JsonValue v)
            {
                string s;
                if (v.TryGetValue(out s)) return s;
                return v.ToJsonString();
            }
            return n.ToJsonString();
        }

        public string Need(string name)
        {
            string s = Str(name);
            if (string.IsNullOrEmpty(s)) throw new ArgumentException("'" + name + "' is required");
            return s;
        }

        public double? Num(string name)
        {
            JsonNode n = _o[name];
            if (n == null) return null;
            if (n is JsonValue v)
            {
                double d;
                if (v.TryGetValue(out d)) return d;
                string s;
                if (v.TryGetValue(out s) && double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            }
            throw new ArgumentException("'" + name + "' must be a number");
        }

        public double Num(string name, double fallback) { return Num(name) ?? fallback; }

        public bool Bool(string name, bool fallback = false)
        {
            JsonNode n = _o[name];
            if (n == null) return fallback;
            if (n is JsonValue v)
            {
                bool b;
                if (v.TryGetValue(out b)) return b;
                string s;
                if (v.TryGetValue(out s)) return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            }
            throw new ArgumentException("'" + name + "' must be true or false");
        }

        /// A string array; a single string counts as a one-item list.
        public List<string> List(string name)
        {
            List<string> list = new List<string>();
            JsonNode n = _o[name];
            if (n == null) return list;
            if (n is JsonArray a)
            {
                foreach (JsonNode item in a)
                {
                    if (item == null) continue;
                    string s;
                    list.Add(item is JsonValue v && v.TryGetValue(out s) ? s : item.ToJsonString());
                }
                return list;
            }
            // Some clients send an array as its JSON text.
            string text = Str(name);
            if (text.TrimStart().StartsWith("["))
            {
                try
                {
                    if (JsonNode.Parse(text) is JsonArray parsed) return new Args(new JsonObject { [name] = parsed }).List(name);
                }
                catch (System.Text.Json.JsonException) { }
            }
            list.Add(text);
            return list;
        }
    }
}
