using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace SmartXair
{
    public sealed class DetectionResult
    {
        public bool Show;
        public string Reason = "";
        public Point Center;
        public string Process = "";
        public IntPtr Window;        // set only when the foreground window is a target game
        public bool CursorShown;
        public bool CursorClipped;
        public bool SoftwareCursorMenu;
        public bool Aiming;
        public int CursorDistance;   // px from game centre; tells you whether the game recentres the mouse
        public Size ClipSize;        // cursor clip rect; tells you whether the game locks the mouse

        public string Describe()
        {
            return (Show ? "SHOW" : "HIDE") + " (" + Reason + ") fg=" + Process +
                   " cursorShown=" + CursorShown + " clipped=" + CursorClipped +
                   " clip=" + ClipSize.Width + "x" + ClipSize.Height + " distFromCentre=" + CursorDistance +
                   " softMenu=" + SoftwareCursorMenu + " aiming=" + Aiming +
                   " center=" + Center.X + "," + Center.Y;
        }
    }

    /// <summary>
    /// Decides whether the crosshair is wanted, using only public window/cursor state:
    ///  1. game window must be the focused, non-minimised foreground window (else: tabbed out)
    ///  2. a visible OS cursor means a menu
    ///  3. a hidden cursor that wanders away from the window centre means a game-drawn (software) cursor menu
    ///  4. optional aim-key rule
    /// </summary>
    public sealed class Detector
    {
        private const int RecenterRadius = 24;   // px: cursor this close to centre counts as "game is recentring it"
        private const int MoveThreshold = 3;     // px per tick that counts as the cursor moving
        private const int TinyClipSize = 64;     // px: clip rect this small means the game locks the mouse

        private static readonly HashSet<string> NonGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "chrome", "msedge", "firefox", "brave", "opera", "vlc", "mpc-hc64", "PotPlayerMini64",
            "ApplicationFrameHost", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "LockApp",
            "Discord", "steamwebhelper", "obs64", "SmartXair", "CursorLab"
        };

        private readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        private readonly Dictionary<uint, string> names = new Dictionary<uint, string>();
        private DateTime namesResetAt = DateTime.UtcNow;
        private Native.POINT lastCursor;
        private bool softMenu;
        private IntPtr lastWindow = IntPtr.Zero;

        public DetectionResult Evaluate(DetectionSettings s, KeyCombo aimKey)
        {
            var r = new DetectionResult();
            IntPtr hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return Hide(r, "no foreground window");

            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            if (pid == ownPid) return Hide(r, "SmartXair window focused");
            r.Process = ProcessName(pid);

            if (Native.IsIconic(hwnd)) return Hide(r, "game minimised");
            if (!IsTargetGame(hwnd, r.Process, s)) return Hide(r, "tabbed out / not a game");
            r.Window = hwnd;

            if (hwnd != lastWindow)
            {
                // Fresh focus: forget software-cursor state from the previous window.
                softMenu = false;
                lastWindow = hwnd;
            }

            Native.RECT client;
            if (!Native.GetClientRect(hwnd, out client) || client.Width <= 0 || client.Height <= 0)
                return Hide(r, "game window has no client area");
            var c = new Native.POINT(client.Width / 2, client.Height / 2);
            Native.ClientToScreen(hwnd, ref c);
            r.Center = new Point(c.X, c.Y);

            var ci = new Native.CURSORINFO();
            ci.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.CURSORINFO));
            bool haveCursor = Native.GetCursorInfo(ref ci);
            r.CursorShown = haveCursor && (ci.flags & Native.CURSOR_SHOWING) != 0 && ci.hCursor != IntPtr.Zero;
            Native.RECT clip;
            if (Native.GetClipCursor(out clip)) r.ClipSize = new Size(clip.Width, clip.Height);
            r.CursorClipped = r.ClipSize.Width > 0 && r.ClipSize.Width <= TinyClipSize && r.ClipSize.Height <= TinyClipSize;

            var pos = haveCursor ? ci.ptScreenPos : lastCursor;
            r.CursorDistance = (int)Math.Sqrt((double)(pos.X - c.X) * (pos.X - c.X) + (double)(pos.Y - c.Y) * (pos.Y - c.Y));
            UpdateSoftwareCursor(pos, c, r.CursorShown, r.CursorClipped);
            r.SoftwareCursorMenu = softMenu;
            r.Aiming = aimKey != null && aimKey.IsDown();

            if (s.HideWhenCursorVisible && r.CursorShown) return Hide(r, "menu: cursor visible");
            if (s.HideOnSoftwareCursor && softMenu) return Hide(r, "menu: game-drawn cursor moving");
            if (s.Aim == AimMode.HideWhileAiming && r.Aiming) return Hide(r, "aiming (hide-while-aiming)");
            if (s.Aim == AimMode.OnlyWhileAiming && !r.Aiming) return Hide(r, "not aiming (only-while-aiming)");

            r.Show = true;
            r.Reason = "in game";
            return r;
        }

        private void UpdateSoftwareCursor(Native.POINT pos, Native.POINT center, bool cursorShown, bool clipped)
        {
            int dx = pos.X - center.X, dy = pos.Y - center.Y;
            bool nearCenter = dx * dx + dy * dy <= RecenterRadius * RecenterRadius;
            int mx = pos.X - lastCursor.X, my = pos.Y - lastCursor.Y;
            bool moved = mx * mx + my * my >= MoveThreshold * MoveThreshold;
            lastCursor = pos;

            if (cursorShown) { softMenu = false; return; }  // OS cursor rule covers this case
            if (clipped || nearCenter) { softMenu = false; return; } // game is locking/recentring the mouse = gameplay
            if (moved) softMenu = true;                     // hidden cursor roaming freely = game-drawn menu cursor
        }

        private bool IsTargetGame(IntPtr hwnd, string process, DetectionSettings s)
        {
            foreach (var g in s.Games)
                if (string.Equals(g, process, StringComparison.OrdinalIgnoreCase)) return true;
            if (!s.AutoFullscreen || NonGames.Contains(process)) return false;
            if (hwnd == Native.GetShellWindow() || IsDesktopClass(hwnd)) return false;
            return CoversMonitor(hwnd);
        }

        private static bool IsDesktopClass(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd";
        }

        private static bool CoversMonitor(IntPtr hwnd)
        {
            Native.RECT wr;
            if (!Native.GetWindowRect(hwnd, out wr)) return false;
            var mi = new Native.MONITORINFO();
            mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi)) return false;
            var m = mi.rcMonitor;
            return wr.Left <= m.Left && wr.Top <= m.Top && wr.Right >= m.Right && wr.Bottom >= m.Bottom;
        }

        private string ProcessName(uint pid)
        {
            if ((DateTime.UtcNow - namesResetAt).TotalSeconds > 30 || names.Count > 256)
            {
                names.Clear();
                namesResetAt = DateTime.UtcNow;
            }
            string n;
            if (names.TryGetValue(pid, out n)) return n;
            try
            {
                using (var p = Process.GetProcessById((int)pid)) n = p.ProcessName;
            }
            catch (ArgumentException) { n = "?"; }
            catch (InvalidOperationException) { n = "?"; }
            names[pid] = n;
            return n;
        }

        private static DetectionResult Hide(DetectionResult r, string reason)
        {
            r.Show = false;
            r.Reason = reason;
            return r;
        }
    }

    /// <summary>Hides instantly (no flash when a menu opens) but only shows after the "show" state is stable.</summary>
    public sealed class VisibilityGate
    {
        private DateTime showCandidateSince = DateTime.MinValue;
        public bool Visible { get; private set; }

        public bool Update(bool wantShow, int showDelayMs, DateTime now)
        {
            if (!wantShow)
            {
                showCandidateSince = DateTime.MinValue;
                Visible = false;
                return Visible;
            }
            if (showCandidateSince == DateTime.MinValue) showCandidateSince = now;
            if ((now - showCandidateSince).TotalMilliseconds >= showDelayMs) Visible = true;
            return Visible;
        }
    }

    /// <summary>"F8", "Ctrl+Shift+X", "RButton", "XButton2". Polled with GetAsyncKeyState so the key still reaches the game.</summary>
    public sealed class KeyCombo
    {
        private readonly List<Keys> keys;
        private bool wasDown;

        private KeyCombo(List<Keys> keys) { this.keys = keys; }

        public static KeyCombo Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var list = new List<Keys>();
            foreach (var raw in text.Split('+'))
            {
                string t = raw.Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase)) list.Add(Keys.ControlKey);
                else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) list.Add(Keys.ShiftKey);
                else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) list.Add(Keys.Menu);
                else
                {
                    Keys k;
                    if (!Enum.TryParse(t, true, out k) || k == Keys.None) return null;
                    list.Add(k);
                }
            }
            return list.Count == 0 ? null : new KeyCombo(list);
        }

        public bool IsDown()
        {
            foreach (var k in keys)
                if ((Native.GetAsyncKeyState((int)k) & 0x8000) == 0) return false;
            return true;
        }

        /// <summary>True once per press (rising edge).</summary>
        public bool Pressed()
        {
            bool down = IsDown();
            bool edge = down && !wasDown;
            wasDown = down;
            return edge;
        }
    }
}
