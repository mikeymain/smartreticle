using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SmartXair
{
    internal sealed class SettingsForm : Form
    {
        private readonly TrayApp app;
        private readonly AppConfig cfg;
        private readonly PictureBox preview = new PictureBox();
        private readonly Label status = new Label();
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly TextBox gamesBox = new TextBox();
        private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        private TableLayoutPanel grid;
        private bool loading;

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            cfg = app.Config;
            Text = "SmartXair - Settings";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(760, Math.Min(820, Screen.PrimaryScreen.WorkingArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen;

            loading = true;
            BuildLayout();
            loading = false;
            RenderPreview();

            statusTimer.Interval = 200;
            statusTimer.Tick += (s, e) => UpdateStatus();
            statusTimer.Start();
            FormClosed += (s, e) => statusTimer.Stop();
        }

        public void RefreshEnabled()
        {
            loading = true;
            enabledBox.Checked = cfg.Enabled;
            loading = false;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);

            grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(grid, 0, 0);

            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            root.Controls.Add(right, 1, 0);

            BuildCrosshairRows();
            BuildDetectionRows();
            BuildRightPanel(right);
        }

        private void BuildCrosshairRows()
        {
            var c = cfg.Crosshair;
            Header("Crosshair");
            Combo("Style", Enum.GetNames(typeof(CrosshairStyle)), c.Style.ToString(),
                v => c.Style = (CrosshairStyle)Enum.Parse(typeof(CrosshairStyle), v));
            ColorRow("Color", () => c.Color, v => c.Color = v);
            Num("Arm length", 0, 100, () => c.Length, v => c.Length = v);
            Num("Gap", 0, 100, () => c.Gap, v => c.Gap = v);
            Num("Thickness", 1, 20, () => c.Thickness, v => c.Thickness = v);
            Num("Dot size", 1, 30, () => c.DotSize, v => c.DotSize = v);
            Num("Circle radius", 2, 150, () => c.CircleRadius, v => c.CircleRadius = v);
            Check("Outline", c.Outline, v => c.Outline = v);
            Num("Outline thickness", 1, 6, () => c.OutlineThickness, v => c.OutlineThickness = v);
            ColorRow("Outline color", () => c.OutlineColor, v => c.OutlineColor = v);
            Num("Opacity %", 5, 100, () => c.Opacity, v => c.Opacity = v);
            Num("Offset X (px)", -500, 500, () => c.OffsetX, v => c.OffsetX = v);
            Num("Offset Y (px)", -500, 500, () => c.OffsetY, v => c.OffsetY = v);
        }

        private void BuildDetectionRows()
        {
            var d = cfg.Detection;
            Header("When to show");
            gamesBox.Text = string.Join(", ", d.Games.ToArray());
            gamesBox.Width = 230;
            gamesBox.Leave += (s, e) => SaveGames();
            Row("Games (exe names)", gamesBox);

            var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            pick.DropDown += (s, e) => FillRunningWindows(pick);
            pick.SelectionChangeCommitted += (s, e) => AddGame(pick.SelectedItem as string);
            Row("Add running game", pick);

            Check("Force borderless (set game to Windowed)", d.ForceBorderless, v => d.ForceBorderless = v);
            Check("Hide when mouse cursor shows (menus)", d.HideWhenCursorVisible, v => d.HideWhenCursorVisible = v);
            Check("Hide when game-drawn cursor moves", d.HideOnSoftwareCursor, v => d.HideOnSoftwareCursor = v);
            Check("Auto-detect any fullscreen game", d.AutoFullscreen, v => d.AutoFullscreen = v);
            Combo("Aim behaviour", Enum.GetNames(typeof(AimMode)), d.Aim.ToString(),
                v => d.Aim = (AimMode)Enum.Parse(typeof(AimMode), v));
            KeyRow("Aim key", () => d.AimKey, v => d.AimKey = v);
            KeyRow("On/off hotkey", () => d.ToggleHotkey, v => d.ToggleHotkey = v);
            Num("Show delay (ms)", 0, 2000, () => d.ShowDelayMs, v => d.ShowDelayMs = v);
            Check("Write debug log", d.DebugLog, v => d.DebugLog = v);
        }

        private void BuildRightPanel(FlowLayoutPanel p)
        {
            enabledBox.Text = "Overlay enabled";
            enabledBox.AutoSize = true;
            enabledBox.Checked = cfg.Enabled;
            enabledBox.CheckedChanged += (s, e) => { if (!loading) app.SetEnabled(enabledBox.Checked); };
            p.Controls.Add(enabledBox);

            preview.Size = new Size(290, 230);
            preview.BackColor = Color.FromArgb(48, 52, 58);
            preview.SizeMode = PictureBoxSizeMode.CenterImage;
            p.Controls.Add(preview);

            var bgRow = new FlowLayoutPanel { AutoSize = true };
            foreach (var bg in new[] { Color.FromArgb(48, 52, 58), Color.FromArgb(200, 205, 210), Color.FromArgb(150, 110, 60), Color.FromArgb(40, 90, 50) })
            {
                var color = bg;
                var b = new Button { BackColor = color, Width = 30, Height = 22, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => preview.BackColor = color;
                bgRow.Controls.Add(b);
            }
            p.Controls.Add(Lbl("Preview background (zoom 3x):"));
            p.Controls.Add(bgRow);

            var shareRow = new FlowLayoutPanel { AutoSize = true };
            var copy = new Button { Text = "Copy share code", AutoSize = true };
            copy.Click += (s, e) => Clipboard.SetText(AppConfig.ToShareCode(cfg.Crosshair));
            var paste = new Button { Text = "Paste share code", AutoSize = true };
            paste.Click += (s, e) => PasteShareCode();
            shareRow.Controls.Add(copy);
            shareRow.Controls.Add(paste);
            p.Controls.Add(shareRow);

            var logBtn = new Button { Text = "Open debug log", AutoSize = true };
            logBtn.Click += (s, e) => OpenLog();
            p.Controls.Add(logBtn);

            p.Controls.Add(Lbl("Live detection (last game window):"));
            status.AutoSize = false;
            status.Size = new Size(290, 140);
            status.Font = new Font("Consolas", 8.5f);
            p.Controls.Add(status);

            p.Controls.Add(Lbl("Set the game to Windowed at your monitor's resolution.\n'Force borderless' makes it fullscreen without a frame.\nExclusive fullscreen hides every overlay."));
        }

        // ---------- row helpers ----------

        private void Header(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };
            grid.Controls.Add(l);
            grid.SetColumnSpan(l, 2);
        }

        private void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 0) });
            grid.Controls.Add(c);
        }

        private void Num(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, get())), Width = 80 };
            n.ValueChanged += (s, e) => Changed(() => set((int)n.Value));
            Row(label, n);
        }

        private void Check(string label, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = label, Checked = value, AutoSize = true };
            c.CheckedChanged += (s, e) => Changed(() => set(c.Checked));
            grid.Controls.Add(c);
            grid.SetColumnSpan(c, 2);
        }

        private void Combo(string label, string[] items, string current, Action<string> set)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            cb.Items.AddRange(items);
            cb.SelectedItem = current;
            cb.SelectedIndexChanged += (s, e) => Changed(() => set((string)cb.SelectedItem));
            Row(label, cb);
        }

        private void ColorRow(string label, Func<Color> get, Action<Color> set)
        {
            var b = new Button { Width = 80, Height = 24, BackColor = get(), FlatStyle = FlatStyle.Flat };
            b.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = get(), FullOpen = true })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    b.BackColor = dlg.Color;
                    Changed(() => set(dlg.Color));
                }
            };
            Row(label, b);
        }

        /// <summary>Click the box, press a key or mouse button (Esc cancels).</summary>
        private void KeyRow(string label, Func<string> get, Action<string> set)
        {
            var t = new TextBox { Text = get(), ReadOnly = true, Width = 160, Cursor = Cursors.Hand };
            t.KeyDown += (s, e) =>
            {
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape || IsModifierOnly(e.KeyCode)) return;
                string combo = (e.Control ? "Ctrl+" : "") + (e.Shift ? "Shift+" : "") + (e.Alt ? "Alt+" : "") + e.KeyCode;
                t.Text = combo;
                Changed(() => set(combo));
            };
            t.MouseDown += (s, e) =>
            {
                if (!t.Focused) return; // first click only focuses the box
                string btn = e.Button == MouseButtons.Right ? "RButton" : e.Button == MouseButtons.Middle ? "MButton" :
                             e.Button == MouseButtons.XButton1 ? "XButton1" : e.Button == MouseButtons.XButton2 ? "XButton2" : null;
                if (btn == null) return;
                t.Text = btn;
                Changed(() => set(btn));
            };
            Row(label, t);
        }

        private static bool IsModifierOnly(Keys k)
        {
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu;
        }

        private static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        }

        // ---------- actions ----------

        private void Changed(Action apply)
        {
            if (loading) return;
            apply();
            cfg.Save();
            app.ApplySettings();
            RenderPreview();
        }

        private void SaveGames()
        {
            var list = gamesBox.Text.Split(',').Select(DetectionSettings.NormalizeExe)
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Changed(() => cfg.Detection.Games = list);
            gamesBox.Text = string.Join(", ", list.ToArray());
        }

        private void AddGame(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            gamesBox.Text = gamesBox.Text.Trim().Length == 0 ? name : gamesBox.Text + ", " + name;
            SaveGames();
        }

        private static void FillRunningWindows(ComboBox cb)
        {
            cb.Items.Clear();
            var names = Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } })
                .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            cb.Items.AddRange(names);
        }

        private void PasteShareCode()
        {
            var s = AppConfig.FromShareCode(Clipboard.GetText());
            if (s == null)
            {
                MessageBox.Show(this, "Clipboard does not hold a SmartXair share code (starts with SR1-).", "SmartXair");
                return;
            }
            cfg.Crosshair = s;
            cfg.Save();
            app.ApplySettings();
            // Rebuild so every control shows the pasted values.
            loading = true;
            Controls.Clear();
            BuildLayout();
            loading = false;
            RenderPreview();
        }

        private void OpenLog()
        {
            string path = Path.Combine(AppConfig.Dir, "debug.log");
            if (!File.Exists(path)) Log.Write("Log opened. Enable 'Write debug log' to record detection changes.");
            Process.Start("notepad.exe", "\"" + path + "\"");
        }

        private void UpdateStatus()
        {
            var r = app.LastExternal;
            if (r == null) { status.Text = "(switch to the game once)"; return; }
            status.Text = (r.Show ? "WOULD SHOW" : "HIDDEN") + "\r\n" +
                          "reason:  " + r.Reason + "\r\n" +
                          "window:  " + r.Process + "\r\n" +
                          "cursor:  " + (r.CursorShown ? "visible" : "hidden") + (r.CursorClipped ? ", locked" : "") + "\r\n" +
                          "clip:    " + r.ClipSize.Width + "x" + r.ClipSize.Height + "\r\n" +
                          "from ctr:" + r.CursorDistance + " px\r\n" +
                          "softcur: " + (r.SoftwareCursorMenu ? "menu-like" : "no") + "\r\n" +
                          "aiming:  " + (r.Aiming ? "yes" : "no");
        }

        private void RenderPreview()
        {
            const int zoom = 3;
            using (var src = CrosshairRenderer.Render(cfg.Crosshair))
            {
                var img = new Bitmap(src.Width * zoom, src.Height * zoom, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(img))
                using (var attrs = new ImageAttributes())
                {
                    var cm = new ColorMatrix { Matrix33 = cfg.Crosshair.Opacity / 100f };
                    attrs.SetColorMatrix(cm);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(0, 0, img.Width, img.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                }
                var old = preview.Image;
                preview.Image = img;
                if (old != null) old.Dispose();
            }
        }
    }
}
