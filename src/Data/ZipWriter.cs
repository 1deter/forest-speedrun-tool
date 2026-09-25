using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A minimal zip writer for the QA report (v0.24.56).
    //
    // net35 has no ZipFile, and Unity 5.6's Mono DeflateStream needs a
    // native helper (MonoPosixHelper) the game may not ship - so entries
    // are STORED, not compressed. Logs and savestates are a few MB; the
    // point is one file to send, not size. No zip64: entries and the whole
    // archive stay far below 4 GB.
    //
    // Pure (a Stream in, bytes out), tested by reading the result back
    // with the framework's ZipArchive.
    // ------------------------------------------------------------------
    public sealed class ZipWriter : IDisposable
    {
        private sealed class Entry
        {
            public byte[] Name;
            public uint Crc;
            public uint Size;
            public uint Offset;
            public ushort Time, Date;
        }

        private readonly Stream _out;
        private readonly List<Entry> _entries = new List<Entry>();
        private bool _finished;

        public ZipWriter(Stream output) { _out = output; }

        public int Count { get { return _entries.Count; } }

        public void Add(string name, byte[] data, DateTime modified)
        {
            if (_finished) throw new InvalidOperationException("zip already finished");

            Entry e = new Entry();
            e.Name = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
            e.Crc = Crc32(data);
            e.Size = (uint)data.Length;
            e.Offset = (uint)_out.Position;
            DosTime(modified, out e.Time, out e.Date);

            // Local file header.
            U32(0x04034b50);
            U16(20);            // version needed
            U16(0x0800);        // flags: UTF-8 names
            U16(0);             // method: stored
            U16(e.Time); U16(e.Date);
            U32(e.Crc); U32(e.Size); U32(e.Size);
            U16((ushort)e.Name.Length); U16(0);
            _out.Write(e.Name, 0, e.Name.Length);
            _out.Write(data, 0, data.Length);

            _entries.Add(e);
        }

        public void Add(string name, string text, DateTime modified)
        {
            Add(name, new UTF8Encoding(false).GetBytes(text), modified);
        }

        /// Writes the central directory. Called by Dispose if not before.
        public void Finish()
        {
            if (_finished) return;
            _finished = true;

            uint start = (uint)_out.Position;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                U32(0x02014b50);
                U16(20); U16(20);
                U16(0x0800); U16(0);
                U16(e.Time); U16(e.Date);
                U32(e.Crc); U32(e.Size); U32(e.Size);
                U16((ushort)e.Name.Length); U16(0); U16(0);
                U16(0); U16(0); U32(0);
                U32(e.Offset);
                _out.Write(e.Name, 0, e.Name.Length);
            }
            uint size = (uint)_out.Position - start;

            U32(0x06054b50);
            U16(0); U16(0);
            U16((ushort)_entries.Count); U16((ushort)_entries.Count);
            U32(size); U32(start);
            U16(0);
            _out.Flush();
        }

        public void Dispose() { Finish(); }

        // --------------------------------------------------------------
        private void U16(ushort v)
        {
            _out.WriteByte((byte)v);
            _out.WriteByte((byte)(v >> 8));
        }

        private void U32(uint v)
        {
            _out.WriteByte((byte)v);
            _out.WriteByte((byte)(v >> 8));
            _out.WriteByte((byte)(v >> 16));
            _out.WriteByte((byte)(v >> 24));
        }

        private static void DosTime(DateTime t, out ushort time, out ushort date)
        {
            if (t.Year < 1980) t = new DateTime(1980, 1, 1);
            time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
            date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        }

        private static uint[] _table;

        public static uint Crc32(byte[] data)
        {
            if (_table == null)
            {
                uint[] table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint c = i;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[i] = c;
                }
                _table = table;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++) crc = _table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
