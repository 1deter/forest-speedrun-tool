using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Which survival book pages are showing, as a savestate header value.
    //
    // The book has no "current page" number: turning a page deactivates
    // one page object and activates another (SelectPageNumber.OnClick,
    // game-notes *Survival book*). So the state is the on/off of every page
    // object, in the fixed order Game/BookPages lists them:
    //
    //   book = 23:00000100000000000000001
    //
    // The count comes first so a restore into a different page layout (a
    // game update) is refused instead of switching the wrong pages on.
    // ------------------------------------------------------------------
    public static class BookPageState
    {
        public static string Encode(IList<bool> active)
        {
            StringBuilder sb = new StringBuilder(active.Count + 6);
            sb.Append(active.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
            for (int i = 0; i < active.Count; i++) sb.Append(active[i] ? '1' : '0');
            return sb.ToString();
        }

        /// False when the value is malformed or lists another number of
        /// pages than `expected`; `why` then says which.
        public static bool TryDecode(string value, int expected, out bool[] active, out string why)
        {
            active = null;
            why = null;

            if (string.IsNullOrEmpty(value)) { why = "no book state"; return false; }

            int colon = value.IndexOf(':');
            int count;
            if (colon <= 0 ||
                !int.TryParse(value.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out count) ||
                value.Length - colon - 1 != count)
            {
                why = "malformed book state";
                return false;
            }

            if (count != expected)
            {
                why = "the book has " + expected + " page objects, the savestate " + count;
                return false;
            }

            bool[] bits = new bool[count];
            for (int i = 0; i < count; i++)
            {
                char c = value[colon + 1 + i];
                if (c != '0' && c != '1') { why = "malformed book state"; return false; }
                bits[i] = c == '1';
            }

            active = bits;
            return true;
        }
    }
}
