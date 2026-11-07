using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SmartXair
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "SmartXair.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("SmartXair is already running (check the system tray).", "SmartXair");
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI error: " + e.Exception);
                Application.Run(new TrayApp(Array.IndexOf(args, "--minimized") < 0));
            }
        }
    }

    /// <summary>Owns the tray icon, the overlay and the 60 Hz detection loop.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private const int TickMs = 16;

        private readonly AppConfig config;
        private readonly Detector detector = new Detector();
        private readonly VisibilityGate gate = new VisibilityGate();
        private readonly BorderlessForcer borderless = new BorderlessForcer();
        private readonly OverlayWindow overlay = new OverlayWindow();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly ToolStripMenuItem enabledItem;
        private SettingsForm settings;
        private KeyCombo toggleKey;
        private KeyCombo aimKey;
        private string lastLogged = "";
        private Icon trayIcon;

        public DetectionResult LastExternal { get; private set; }

        public TrayApp(bool openSettings)
        {
            config = AppConfig.Load();
            ApplySettings();

            overlay.Show();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Settings...", null, (s, e) => OpenSettings());
            enabledItem = new ToolStripMenuItem("Enabled", null, (s, e) => SetEnabled(!config.Enabled));
            enabledItem.Checked = config.Enabled;
            menu.Items.Add(enabledItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => OpenSettings();
            tray.Visible = true;
            UpdateTray();

            timer.Interval = TickMs;
            timer.Tick += (s, e) => Tick();
            timer.Start();

            if (openSettings) OpenSettings();
        }

        public AppConfig Config { get { return config; } }

        /// <summary>Called by the settings window after every change.</summary>
        public void ApplySettings()
        {
            overlay.SetCrosshair(config.Crosshair);
            toggleKey = KeyCombo.Parse(config.Detection.ToggleHotkey);
            aimKey = KeyCombo.Parse(config.Detection.AimKey);
            if (trayIcon != null) { Native.DestroyIcon(trayIcon.Handle); trayIcon.Dispose(); }
            using (var bmp = CrosshairRenderer.Render(config.Crosshair))
            using (var small = new Bitmap(bmp, 32, 32))
            {
                trayIcon = Icon.FromHandle(small.GetHicon());
            }
            tray.Icon = trayIcon;
        }

        public void SetEnabled(bool on)
        {
            config.Enabled = on;
            config.Save();
            UpdateTray();
            if (settings != null && !settings.IsDisposed) settings.RefreshEnabled();
        }

        private void UpdateTray()
        {
            enabledItem.Checked = config.Enabled;
            tray.Text = "SmartXair - " + (config.Enabled ? "on" : "off") + " (" + config.Detection.ToggleHotkey + " toggles)";
        }

        private void Tick()
        {
            if (toggleKey != null && toggleKey.Pressed()) SetEnabled(!config.Enabled);

            var r = detector.Evaluate(config.Detection, aimKey);
            if (config.Detection.ForceBorderless && r.Window != IntPtr.Zero) borderless.TryApply(r.Window);
            if (r.Reason != "SmartXair window focused") LastExternal = r;

            bool want = config.Enabled && r.Show;
            bool visible = gate.Update(want, config.Detection.ShowDelayMs, DateTime.UtcNow);
            var c = new Point(r.Center.X + config.Crosshair.OffsetX, r.Center.Y + config.Crosshair.OffsetY);
            overlay.Place(c, visible);

            if (config.Detection.DebugLog)
            {
                string line = (config.Enabled ? "" : "[disabled] ") + r.Describe();
                // Log only state changes, not 60 lines a second.
                string key = r.Show + r.Reason + r.Process + r.CursorShown + r.SoftwareCursorMenu + r.Aiming;
                if (key != lastLogged) { Log.Write(line); lastLogged = key; }
            }
        }

        private void OpenSettings()
        {
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
            settings.Show();
            settings.WindowState = FormWindowState.Normal;
            settings.Activate();
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            config.Save();
            tray.Visible = false;
            tray.Dispose();
            overlay.Close();
            if (settings != null && !settings.IsDisposed) settings.Close();
            base.ExitThreadCore();
        }
    }
}
