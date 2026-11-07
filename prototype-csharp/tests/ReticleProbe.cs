using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace SmartXair
{
    /// <summary>
    ///   ReticleProbe.exe unit                 pure-logic tests (config, share codes, keys, debounce, renderer)
    ///   ReticleProbe.exe render &lt;dir&gt;         PNG of every style for eyeballing
    ///   ReticleProbe.exe watch &lt;sec&gt; &lt;game&gt;  print detector decisions as they change (run CursorLab alongside)
    /// </summary>
    internal static class ReticleProbe
    {
        private static int failures;

        private static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "unit";
            if (cmd == "render") return Render(args.Length > 1 ? args[1] : "render");
            if (cmd == "watch")
                return Watch(args.Length > 1 ? int.Parse(args[1]) : 5, args.Length > 2 ? args[2] : "CursorLab",
                    args.Length > 3 && args[3] == "soft");
            return Unit();
        }

        private static void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok) failures++;
        }

        private static int Unit()
        {
            Check(DetectionSettings.NormalizeExe(@"C:\Games\TheDivision2.exe") == "TheDivision2", "NormalizeExe strips path and .exe");
            Check(DetectionSettings.NormalizeExe("  ") == "", "NormalizeExe blank");

            var s = new CrosshairSettings { Style = CrosshairStyle.CircleCross, Gap = 7, Color = Color.FromArgb(255, 10, 20), Opacity = 55 };
            var back = AppConfig.FromShareCode(AppConfig.ToShareCode(s));
            Check(back != null && back.Style == s.Style && back.Gap == 7 && back.Opacity == 55 && back.Color.ToArgb() == s.Color.ToArgb(),
                "share code round-trip");
            Check(AppConfig.FromShareCode("garbage") == null, "bad share code rejected");
            Check(AppConfig.FromShareCode("SR1-!!!notbase64") == null, "corrupt share code rejected");

            var d = Cfg.Parse(new[] { "gap=9999", "thickness=-5", "style=Nope", "outline=0", "# comment", "junk" });
            var c = new CrosshairSettings();
            c.Read(d);
            Check(c.Gap == 100 && c.Thickness == 1 && c.Style == CrosshairStyle.CrossDot && !c.Outline, "config values clamped / invalid ignored");

            var det = new DetectionSettings();
            Check(det.Games.Count == 1 && det.Games[0] == "TheDivision2", "Division 2 is the default game");
            var dd = new Dictionary<string, string>();
            det.Aim = AimMode.OnlyWhileAiming;
            det.Write(dd);
            var det2 = new DetectionSettings();
            det2.Read(dd);
            Check(det2.Aim == AimMode.OnlyWhileAiming && det2.Games[0] == "TheDivision2", "detection settings round-trip");

            Check(KeyCombo.Parse("F8") != null, "parse F8");
            Check(KeyCombo.Parse("Ctrl+Shift+X") != null, "parse Ctrl+Shift+X");
            Check(KeyCombo.Parse("RButton") != null, "parse RButton");
            Check(KeyCombo.Parse("NotAKey") == null, "reject unknown key");

            var gate = new VisibilityGate();
            var t0 = new DateTime(2026, 1, 1);
            Check(!gate.Update(true, 120, t0), "gate: not shown immediately");
            Check(!gate.Update(true, 120, t0.AddMilliseconds(100)), "gate: still hidden at 100ms");
            Check(gate.Update(true, 120, t0.AddMilliseconds(130)), "gate: shown after 120ms stable");
            Check(!gate.Update(false, 120, t0.AddMilliseconds(140)), "gate: hides instantly");
            Check(!gate.Update(true, 120, t0.AddMilliseconds(150)), "gate: delay restarts after hide");

            foreach (CrosshairStyle st in Enum.GetValues(typeof(CrosshairStyle)))
            {
                using (var bmp = CrosshairRenderer.Render(new CrosshairSettings { Style = st }))
                {
                    int mid = bmp.Width / 2;
                    Check(bmp.Width % 2 == 1 && HasInk(bmp), "render " + st + " (" + bmp.Width + "px, odd, non-empty)");
                    if (st == CrosshairStyle.Dot || st == CrosshairStyle.CrossDot || st == CrosshairStyle.CircleDot)
                        Check(bmp.GetPixel(mid, mid).A == 255, "render " + st + " has solid centre pixel");
                }
            }
            var noOutline = CrosshairRenderer.Render(new CrosshairSettings { Style = CrosshairStyle.Cross, Gap = 4, Outline = false });
            int m = noOutline.Width / 2;
            Check(noOutline.GetPixel(m, m).A == 0, "cross gap leaves centre empty");
            Check(SymmetricX(noOutline), "cross arms symmetric left/right");
            noOutline.Dispose();

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static bool HasInk(Bitmap b)
        {
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                    if (b.GetPixel(x, y).A > 0) return true;
            return false;
        }

        private static bool SymmetricX(Bitmap b)
        {
            // Thickness 2 is centred on pixel boundary c-1|c, so mirror around c - 0.5 vertically isn't needed; check horizontal row.
            int row = b.Height / 2;
            int left = 0, right = 0, c = b.Width / 2;
            for (int x = 0; x < c; x++) if (b.GetPixel(x, row).A > 0) left++;
            for (int x = c + 1; x < b.Width; x++) if (b.GetPixel(x, row).A > 0) right++;
            return left == right && left > 0;
        }

        private static int Render(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (CrosshairStyle st in Enum.GetValues(typeof(CrosshairStyle)))
            {
                using (var bmp = CrosshairRenderer.Render(new CrosshairSettings { Style = st }))
                using (var big = new Bitmap(bmp.Width * 6, bmp.Height * 6))
                using (var g = Graphics.FromImage(big))
                {
                    g.Clear(Color.FromArgb(48, 52, 58));
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(bmp, 0, 0, big.Width, big.Height);
                    big.Save(Path.Combine(dir, st + ".png"), ImageFormat.Png);
                }
            }
            Console.WriteLine("Rendered to " + Path.GetFullPath(dir));
            return 0;
        }

        private static int Watch(int seconds, string game, bool softCursorRule)
        {
            var det = new Detector();
            var s = new DetectionSettings { Games = new List<string> { game }, HideOnSoftwareCursor = softCursorRule };
            var gate = new VisibilityGate();
            string last = null;
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                var r = det.Evaluate(s, KeyCombo.Parse(s.AimKey));
                bool vis = gate.Update(r.Show, s.ShowDelayMs, DateTime.UtcNow);
                string line = (vis ? "VISIBLE " : "hidden  ") + r.Describe();
                string key = vis + r.Reason + r.Process + r.Show;
                if (key != last) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); last = key; }
                Thread.Sleep(16);
            }
            return 0;
        }
    }
}
