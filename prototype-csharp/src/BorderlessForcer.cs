using System;
using System.Collections.Generic;

namespace SmartXair
{
    /// <summary>
    /// Turns a windowed game into borderless fullscreen: strips the title bar/frame and sizes it to its monitor.
    /// Works from outside the game (window styles only, like "Borderless Gaming"); no injection.
    /// Exclusive-fullscreen windows already have no frame and fill the monitor, so they are left alone.
    /// </summary>
    internal sealed class BorderlessForcer
    {
        private const int CheckIntervalMs = 500;
        private const int MaxAttemptsPerWindow = 5; // stop if the game keeps putting its frame back

        private readonly Dictionary<IntPtr, int> attempts = new Dictionary<IntPtr, int>();
        private DateTime lastCheck = DateTime.MinValue;

        /// <returns>True if the window was changed this call.</returns>
        public bool TryApply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd)) return false;
            if ((DateTime.UtcNow - lastCheck).TotalMilliseconds < CheckIntervalMs) return false;
            lastCheck = DateTime.UtcNow;

            Native.RECT mon;
            if (!MonitorRect(hwnd, out mon)) return false;
            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
            Native.RECT wr;
            if (!Native.GetWindowRect(hwnd, out wr)) return false;

            bool hasFrame = (style & (Native.WS_CAPTION | Native.WS_THICKFRAME)) != 0;
            bool fillsMonitor = wr.Left == mon.Left && wr.Top == mon.Top && wr.Right == mon.Right && wr.Bottom == mon.Bottom;
            if (!hasFrame && fillsMonitor) return false;

            int n;
            attempts.TryGetValue(hwnd, out n);
            if (n >= MaxAttemptsPerWindow) return false;
            attempts[hwnd] = n + 1;
            if (attempts.Count > 64) attempts.Clear();

            long frameBits = Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX;
            long exBits = Native.WS_EX_DLGMODALFRAME | Native.WS_EX_WINDOWEDGE | Native.WS_EX_CLIENTEDGE | Native.WS_EX_STATICEDGE;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();

            Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style & ~frameBits));
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex & ~exBits));
            bool ok = Native.SetWindowPos(hwnd, Native.HWND_TOP, mon.Left, mon.Top, mon.Width, mon.Height,
                Native.SWP_FRAMECHANGED | Native.SWP_NOOWNERZORDER | Native.SWP_NOACTIVATE);

            Log.Write("Borderless: " + (ok ? "applied" : "FAILED (game may run as admin; run SmartXair as admin too)") +
                      " to window " + hwnd + " -> " + mon.Width + "x" + mon.Height + " at " + mon.Left + "," + mon.Top +
                      " (attempt " + (n + 1) + ")");
            return ok;
        }

        private static bool MonitorRect(IntPtr hwnd, out Native.RECT rect)
        {
            var mi = new Native.MONITORINFO();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO));
            bool ok = Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi);
            rect = mi.rcMonitor;
            return ok;
        }
    }
}
