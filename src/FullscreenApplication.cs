using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WheekKeys
{
    internal static class FullscreenApplication
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect bounds, int size);

        internal static bool IsForegroundFullscreen()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || window == GetShellWindow()) return false;
            uint processId;
            GetWindowThreadProcessId(window, out processId);
            if (processId == GetCurrentProcessId()) return false;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            switch (name.ToString())
            {
                case "WorkerW": case "Progman": case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd": return false;
            }
            WindowRect bounds;
            const int extendedFrameBounds = 9;
            if (DwmGetWindowAttribute(window, extendedFrameBounds, out bounds, Marshal.SizeOf(typeof(WindowRect))) != 0 &&
                !GetWindowRect(window, out bounds)) return false;
            Rectangle screen = Screen.FromHandle(window).Bounds;
            return bounds.Left <= screen.Left && bounds.Top <= screen.Top &&
                bounds.Right >= screen.Right && bounds.Bottom >= screen.Bottom;
        }
    }
}
