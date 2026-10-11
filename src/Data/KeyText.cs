namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Text that names a hotkey reads the runner's binding, never the
    // default: a runner who moved Restart to R was told "F7" (T-0262).
    // The caller passes the key's name (KeyCode.ToString()), or null /
    // "" when the action has no key.
    //
    // Pure: linked into the tests.
    // ------------------------------------------------------------------
    public static class KeyText
    {
        /// Where a description names its key.
        public const string Slot = "{key}";

        /// "restart (R)", or just "restart" when the action has no key.
        public static string With(string what, string keyName)
        {
            return string.IsNullOrEmpty(keyName) ? what : what + " (" + keyName + ")";
        }

        /// `template` with its Slot filled by the key; `unbound` when the
        /// action has no key (or the template names none).
        public static string Fill(string template, string keyName, string unbound)
        {
            if (template == null) return unbound;
            if (template.IndexOf(Slot, System.StringComparison.Ordinal) < 0) return template;
            return string.IsNullOrEmpty(keyName) ? unbound : template.Replace(Slot, keyName);
        }
    }
}
