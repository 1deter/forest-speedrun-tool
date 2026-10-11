using System;
using System.Collections.Generic;
using System.IO;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A list that follows its folder by itself (T-0254: no Refresh / Reload
    // buttons). The stamp of the files a list is read from - their names,
    // sizes and write times - is taken when the list is read, and looked at
    // again every few seconds while the list shows; a different stamp
    // reads the list again.
    //
    // WHY a poll and not FileSystemWatcher: the poll runs only while the
    // list is on screen (one folder listing every few seconds, in Tick),
    // nothing runs behind a closed window, and there is no event from
    // another thread to hand back to the main one.
    // ------------------------------------------------------------------
    public sealed class FolderStamp
    {
        private readonly Func<long> _read;
        private readonly float _every;
        private long _stamp;
        private bool _taken;
        private float _nextAt;

        /// The files in `folder` matching any of `patterns` ("*.txt"),
        /// looked at no more than once every `every` seconds.
        public FolderStamp(string folder, float every, params string[] patterns)
            : this(delegate { return Of(folder, patterns); }, every) { }

        public FolderStamp(Func<long> read, float every)
        {
            _read = read;
            _every = every;
        }

        /// Just before the list is read: what it is read from.
        public void Take(float now)
        {
            _stamp = _read();
            _taken = true;
            _nextAt = now + _every;
        }

        /// The next Changed looks at once (the list just came into view).
        public void CheckNow() { _nextAt = float.MinValue; }

        /// True when the files differ from the last Take / Changed - the
        /// list should be read again (the new stamp is kept). Looks at the
        /// folder at most once every `every` seconds; never taken = true.
        public bool Changed(float now)
        {
            if (now < _nextAt) return false;
            _nextAt = now + _every;
            long stamp = _read();
            if (_taken && stamp == _stamp) return false;
            _stamp = stamp;
            _taken = true;
            return true;
        }

        /// The folder's stamp now: 0 when it does not exist, -1 when it
        /// cannot be listed.
        public static long Of(string folder, params string[] patterns)
        {
            try
            {
                if (!Directory.Exists(folder)) return 0L;
                List<string> names = new List<string>();
                List<long> sizes = new List<long>();
                List<long> times = new List<long>();
                for (int p = 0; p < patterns.Length; p++)
                {
                    string[] files = Directory.GetFiles(folder, patterns[p]);
                    for (int i = 0; i < files.Length; i++)
                    {
                        FileInfo fi = new FileInfo(files[i]);
                        names.Add(fi.Name);
                        sizes.Add(fi.Length);
                        times.Add(fi.LastWriteTimeUtc.Ticks);
                    }
                }
                return Hash(names.ToArray(), sizes.ToArray(), times.ToArray());
            }
            catch (Exception) { return -1L; }
        }

        /// One number for a set of files (name, size, write time), whatever
        /// order they were listed in. Names compare without case (Windows).
        public static long Hash(string[] names, long[] sizes, long[] times)
        {
            int n = names.Length;
            string[] keys = new string[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = names[i].ToLowerInvariant();
                order[i] = i;
            }
            Array.Sort(keys, order, StringComparer.Ordinal);

            // FNV-1a, 64 bit.
            ulong h = 14695981039346656037UL;
            h = Mix(h, (ulong)n);
            for (int i = 0; i < n; i++)
            {
                string k = keys[i];
                for (int c = 0; c < k.Length; c++) h = Mix(h, k[c]);
                h = Mix(h, 0UL);
                h = Mix(h, (ulong)sizes[order[i]]);
                h = Mix(h, (ulong)times[order[i]]);
            }
            long result = (long)h;
            return result == 0L || result == -1L ? 1L : result;   // 0 and -1 mean no folder / unreadable
        }

        private static ulong Mix(ulong h, ulong value)
        {
            for (int b = 0; b < 8; b++)
            {
                h ^= value & 0xFF;
                h *= 1099511628211UL;
                value >>= 8;
            }
            return h;
        }
    }
}
