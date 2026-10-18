using System.Globalization;

namespace ForestOverlay.Data
{
    /// The savestate's `spawned` mark (T-0411): the instance id the engine
    /// handed out at the capture. Runtime-made objects get negative ids, each
    /// lower than all before it, so an object with a lower id was made after
    /// the capture. Pure.
    public static class SpawnMark
    {
        public static string Format(string world, int mark)
        {
            if (string.IsNullOrEmpty(world)) return "";
            return world + ":" + mark.ToString(CultureInfo.InvariantCulture);
        }

        /// The mark when the line is for `currentWorld` and parses to a
        /// negative id; else 0 (every runtime-made item counts as new).
        public static int Threshold(string line, string currentWorld)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(currentWorld)) return 0;
            int colon = line.LastIndexOf(':');
            if (colon <= 0 || line.Substring(0, colon) != currentWorld) return 0;
            int n;
            if (!int.TryParse(line.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return 0;
            return n < 0 ? n : 0;
        }

        /// Made after the mark: negative and lower than it (threshold 0 =
        /// every negative id).
        public static bool IsAfter(int instanceId, int threshold)
        {
            return instanceId < 0 && instanceId < threshold;
        }
    }
}
