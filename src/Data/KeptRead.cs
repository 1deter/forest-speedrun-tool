using System;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // When a savestate's read can be kept between restores (T-0202): a
    // restart loop on one spot read, parsed and decompressed the same
    // state every time - ~7 MB of garbage a restore. Two keys, both pure
    // so the staleness rules are tested:
    // - a state FILE is the same while its path (case-insensitive, as
    //   Windows), size and last write time are - a capture over it, or
    //   any copy into its place, changes the write time;
    // - the level DATA is the same text, by reference or by content
    //   (strings are immutable; a slot's save read again is an equal one).
    // SavestateModule.ReadState and SavestateBridge.StoredNames use them,
    // behind the `[Performance]` switch RestoreKeepLastRead.
    // ------------------------------------------------------------------
    public sealed class KeptRead
    {
        private string _path;
        private long _length, _ticks;

        public bool Holds { get { return _path != null; } }

        /// The file at `path` is the one remembered.
        public bool Matches(string path, long length, long writeTicks)
        {
            return _path != null && path != null && length == _length && writeTicks == _ticks &&
                   string.Equals(path, _path, StringComparison.OrdinalIgnoreCase);
        }

        public void Remember(string path, long length, long writeTicks)
        {
            _path = path;
            _length = length;
            _ticks = writeTicks;
        }

        public void Forget() { _path = null; }

        /// `data` is the text `kept` was read from.
        public static bool SameText(string kept, string data)
        {
            return kept != null && data != null &&
                   (ReferenceEquals(data, kept) || (data.Length == kept.Length && string.Equals(data, kept, StringComparison.Ordinal)));
        }
    }
}
