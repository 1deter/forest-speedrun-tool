using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A savestate's `data` line from the level's bytes (T-0072). The game's
    // LevelSerializer.SerializeLevel(bool) builds its string as
    // new StringBuilder("NOCOMPRESSION").Append(Convert.ToBase64String(
    // SerializeLevel(false, null))) (source); a lean checkpoint capture
    // takes the bytes alone and builds the same text here, on a worker
    // thread. Pure, thread-safe.
    // ------------------------------------------------------------------
    public static class LevelText
    {
        public const string Prefix = "NOCOMPRESSION";

        public static string FromBytes(byte[] bytes)
        {
            return Prefix + Convert.ToBase64String(bytes);
        }

        /// The text's length, built (`data`) or not yet (`bytes`); 0 for neither.
        public static int Chars(string data, byte[] bytes)
        {
            if (data != null) return data.Length;
            return bytes != null ? Prefix.Length + (bytes.Length + 2) / 3 * 4 : 0;
        }
    }
}
