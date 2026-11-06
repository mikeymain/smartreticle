using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartReticle
{
    /// <summary>
    /// Fake "game" window that reproduces each cursor state a real game uses, so the detector can be tested without one.
    ///   CursorLab.exe game     cursor hidden + locked to the centre (gameplay)
    ///   CursorLab.exe menu     cursor visible (menu with OS cursor)
    ///   CursorLab.exe softmenu cursor hidden but roaming (menu with game-drawn cursor)
    ///   CursorLab.exe windowed framed 1280x720 window, gameplay cursor (tests Force borderless)
    ///   CursorLab.exe rawgame cursor hidden, clipped to window, roaming (raw-input gameplay; looks like softmenu)
    /// Optional 2nd arg: seconds before auto-exit (default 6).
    /// </summary>
    internal sealed class CursorLab : Form
    {
        private readonly string mode;
        private readonly Timer timer = new Timer();
        private int step;

        private CursorLab(string mode, int seconds)
        {
            this.mode = mode;
            Text = "CursorLab - " + mode;
            if (mode == "windowed")
            {
                // Like Division 2 in Windowed mode: title bar + frame, smaller than the screen; gameplay cursor (hidden, centred).
                FormBorderStyle = FormBorderStyle.Sizable;
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(200, 150, 1280, 720);
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            BackColor = Color.FromArgb(60, 70, 80);
            var exit = new Timer { Interval = seconds * 1000 };
            exit.Tick += (s, e) => Close();
            exit.Start();
            timer.Interval = 30;
            timer.Tick += (s, e) => Step();
            Shown += (s, e) => Begin();
            FormClosed += (s, e) => Native.ReleaseClip(IntPtr.Zero);
        }

        private Point CenterOnScreen()
        {
            return PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        }

        private void Begin()
        {
            Native.SetForegroundWindow(Handle);
            Activate();
            var c = CenterOnScreen();
            Native.SetCursorPos(c.X, c.Y);
            if (mode != "menu") Native.ShowCursor(false);
            if (mode == "game")
            {
                var r = new Native.RECT { Left = c.X, Top = c.Y, Right = c.X + 1, Bottom = c.Y + 1 };
                Native.ClipCursor(ref r);
            }
            else if (mode == "rawgame")
            {
                var tl = PointToScreen(Point.Empty);
                var r = new Native.RECT { Left = tl.X, Top = tl.Y, Right = tl.X + ClientSize.Width, Bottom = tl.Y + ClientSize.Height };
                Native.ClipCursor(ref r);
            }
            timer.Start();
        }

        private void Step()
        {
            step++;
            var c = CenterOnScreen();
            if (mode == "softmenu")
            {
                // Sweep the hidden cursor around like a player moving through an inventory screen.
                double a = step * 0.15;
                Native.SetCursorPos(c.X + (int)(250 * Math.Cos(a)), c.Y + (int)(150 * Math.Sin(a)));
            }
            else if (mode == "game" || mode == "windowed")
            {
                Native.SetCursorPos(c.X, c.Y);
            }
            else if (mode == "rawgame")
            {
                // Raw-input game: cursor hidden, clipped to the window, never recentred; mouse-look drags it around.
                double a = step * 0.08;
                Native.SetCursorPos(c.X + (int)(300 * Math.Sin(a)), c.Y + (int)(80 * Math.Sin(a * 0.7)));
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "game";
            int seconds = args.Length > 1 ? int.Parse(args[1]) : 6;
            Application.Run(new CursorLab(mode, seconds));
        }
    }
}
