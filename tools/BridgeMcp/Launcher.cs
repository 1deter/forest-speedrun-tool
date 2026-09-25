using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // The Forest starts with Unity's launcher dialog ("The Forest
    // Configuration": resolution, quality, input; Play! / Quit), and
    // nothing loads until Play is clicked (author, 2026-09-25). This finds
    // the dialog among TheForest.exe's windows and presses its Play button
    // by sending the dialog the button's WM_COMMAND - no mouse, no focus,
    // so it works behind other windows.
    // ------------------------------------------------------------------
    internal static class Launcher
    {
        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

        private const uint WM_COMMAND = 0x0111;
        private const int BN_CLICKED = 0;

        public sealed class Window
        {
            public IntPtr Handle;
            public string Title, Class;
            public readonly List<(IntPtr handle, string cls, string text)> Children = new List<(IntPtr, string, string)>();
        }

        /// The visible top-level windows of a process, with their children.
        public static List<Window> WindowsOf(int pid)
        {
            List<Window> found = new List<Window>();
            EnumWindows((h, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(h, out owner);
                if (owner != (uint)pid || !IsWindowVisible(h)) return true;
                Window w = new Window { Handle = h, Title = Text(h), Class = Class(h) };
                EnumChildWindows(h, (c, __) => { w.Children.Add((c, Class(c), Text(c))); return true; }, IntPtr.Zero);
                found.Add(w);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// Presses the launcher's Play button if it is showing. Returns what
        /// it clicked, or null when there is no launcher (yet).
        public static string ClickPlay(int pid)
        {
            foreach (Window w in WindowsOf(pid))
                foreach (var c in w.Children)
                {
                    if (!c.cls.Equals("Button", StringComparison.OrdinalIgnoreCase)) continue;
                    string label = c.text.Replace("&", "").Trim();
                    if (!label.StartsWith("Play", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsWindowEnabled(c.handle)) continue;
                    int id = GetDlgCtrlID(c.handle);
                    PostMessage(w.Handle, WM_COMMAND, (IntPtr)((BN_CLICKED << 16) | (id & 0xFFFF)), c.handle);
                    return "'" + label + "' in '" + w.Title + "'";
                }
            return null;
        }

        private static string Text(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string Class(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
