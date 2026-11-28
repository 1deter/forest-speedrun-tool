using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    /// How the tools reach a game: the local files (BridgeClient) or a
    /// runner's game through the site's live relay (RemoteBridge, T-0491).
    internal interface IBridgeTransport
    {
        /// Sends commands and waits for every reply. `readWithin`: how long
        /// the game may take to pick the batch up.
        Task<BridgeReply> Send(IList<string> commands, TimeSpan timeout, CancellationToken ct, TimeSpan? readWithin = null);

        /// The newest "bridge on" banner's plugin version seen, or null.
        string PluginVersionFromBanner();
    }

    internal sealed class BridgeReply
    {
        /// The game took in.txt.
        public bool Delivered;
        /// Every command's reply arrived (the end marker was seen).
        public bool Complete;
        /// Why the call fell short; null when Complete.
        public string Problem;
        public string Raw = "";
        public BridgeText.Transcript Transcript = new BridgeText.Transcript();

        /// The transcript as the model should read it.
        public string Text(bool withHeaders)
        {
            StringBuilder sb = new StringBuilder();
            foreach (object item in Transcript.Items)
            {
                BridgeText.Block b = item as BridgeText.Block;
                if (b == null) { sb.Append((string)item).Append('\n'); continue; }
                if (withHeaders) sb.Append("> ").Append(b.Command).Append('\n');
                foreach (string l in b.Lines) sb.Append(l).Append('\n');
                if (b.Error != null) sb.Append("error: ").Append(b.Error).Append('\n');
                else if (!b.Closed) sb.Append("(no reply yet - still running in game)\n");
                else if (withHeaders) sb.Append("ok (").Append(b.Took).Append(")\n");
            }
            if (Problem != null) sb.Append(Problem).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }
}
