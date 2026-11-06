using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SmartReticle
{
    public enum CrosshairStyle { Cross, CrossDot, TShape, Dot, Circle, CircleDot, CircleCross }

    /// <summary>When the aim key matters. Division 2 players often want the overlay only for hip-fire, or only while aiming.</summary>
    public enum AimMode { Always, HideWhileAiming, OnlyWhileAiming }

    public sealed class CrosshairSettings
    {
        public CrosshairStyle Style = CrosshairStyle.CrossDot;
        public Color Color = Color.FromArgb(0, 255, 140);
        public int Length = 6;
        public int Gap = 4;
        public int Thickness = 2;
        public int DotSize = 2;
        public int CircleRadius = 10;
        public bool Outline = true;
        public int OutlineThickness = 1;
        public Color OutlineColor = Color.Black;
        public int Opacity = 100;
        public int OffsetX = 0;
        public int OffsetY = 0;

        public CrosshairSettings Clone() { return (CrosshairSettings)MemberwiseClone(); }

        public void Write(IDictionary<string, string> d)
        {
            d["style"] = Style.ToString();
            d["color"] = ColorTranslator.ToHtml(Color);
            d["length"] = Fmt(Length);
            d["gap"] = Fmt(Gap);
            d["thickness"] = Fmt(Thickness);
            d["dotSize"] = Fmt(DotSize);
            d["circleRadius"] = Fmt(CircleRadius);
            d["outline"] = Outline ? "1" : "0";
            d["outlineThickness"] = Fmt(OutlineThickness);
            d["outlineColor"] = ColorTranslator.ToHtml(OutlineColor);
            d["opacity"] = Fmt(Opacity);
            d["offsetX"] = Fmt(OffsetX);
            d["offsetY"] = Fmt(OffsetY);
        }

        public void Read(IDictionary<string, string> d)
        {
            Style = Cfg.Enum(d, "style", Style);
            Color = Cfg.Col(d, "color", Color);
            Length = Cfg.Int(d, "length", Length, 0, 100);
            Gap = Cfg.Int(d, "gap", Gap, 0, 100);
            Thickness = Cfg.Int(d, "thickness", Thickness, 1, 20);
            DotSize = Cfg.Int(d, "dotSize", DotSize, 1, 30);
            CircleRadius = Cfg.Int(d, "circleRadius", CircleRadius, 2, 150);
            Outline = Cfg.Bool(d, "outline", Outline);
            OutlineThickness = Cfg.Int(d, "outlineThickness", OutlineThickness, 1, 6);
            OutlineColor = Cfg.Col(d, "outlineColor", OutlineColor);
            Opacity = Cfg.Int(d, "opacity", Opacity, 5, 100);
            OffsetX = Cfg.Int(d, "offsetX", OffsetX, -500, 500);
            OffsetY = Cfg.Int(d, "offsetY", OffsetY, -500, 500);
        }

        private static string Fmt(int v) { return v.ToString(CultureInfo.InvariantCulture); }
    }

    public sealed class DetectionSettings
    {
        public List<string> Games = new List<string> { "TheDivision2" };
        public bool AutoFullscreen = false;
        public bool HideWhenCursorVisible = true;
        // Off by default: raw-input games that clip to the window without recentring look identical to a game-drawn menu cursor.
        public bool HideOnSoftwareCursor = false;
        // Division 2 has no borderless mode: run it Windowed and this strips the frame + fills the monitor.
        public bool ForceBorderless = true;
        public AimMode Aim = AimMode.Always;
        public string AimKey = "RButton";
        public string ToggleHotkey = "F8";
        public int ShowDelayMs = 120;
        public bool DebugLog = false;

        public void Write(IDictionary<string, string> d)
        {
            d["games"] = string.Join(",", Games.ToArray());
            d["autoFullscreen"] = AutoFullscreen ? "1" : "0";
            d["hideWhenCursorVisible"] = HideWhenCursorVisible ? "1" : "0";
            d["hideOnSoftwareCursor"] = HideOnSoftwareCursor ? "1" : "0";
            d["forceBorderless"] = ForceBorderless ? "1" : "0";
            d["aimMode"] = Aim.ToString();
            d["aimKey"] = AimKey;
            d["toggleHotkey"] = ToggleHotkey;
            d["showDelayMs"] = ShowDelayMs.ToString(CultureInfo.InvariantCulture);
            d["debugLog"] = DebugLog ? "1" : "0";
        }

        public void Read(IDictionary<string, string> d)
        {
            string g;
            if (d.TryGetValue("games", out g))
            {
                Games = g.Split(',').Select(NormalizeExe).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            AutoFullscreen = Cfg.Bool(d, "autoFullscreen", AutoFullscreen);
            HideWhenCursorVisible = Cfg.Bool(d, "hideWhenCursorVisible", HideWhenCursorVisible);
            HideOnSoftwareCursor = Cfg.Bool(d, "hideOnSoftwareCursor", HideOnSoftwareCursor);
            ForceBorderless = Cfg.Bool(d, "forceBorderless", ForceBorderless);
            Aim = Cfg.Enum(d, "aimMode", Aim);
            AimKey = Cfg.Str(d, "aimKey", AimKey);
            ToggleHotkey = Cfg.Str(d, "toggleHotkey", ToggleHotkey);
            ShowDelayMs = Cfg.Int(d, "showDelayMs", ShowDelayMs, 0, 2000);
            DebugLog = Cfg.Bool(d, "debugLog", DebugLog);
        }

        /// <summary>"C:\x\TheDivision2.exe" or "thedivision2.EXE" -> "thedivision2" (process-name form).</summary>
        public static string NormalizeExe(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length == 0) return "";
            s = Path.GetFileName(s);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s;
        }
    }

    public sealed class AppConfig
    {
        public CrosshairSettings Crosshair = new CrosshairSettings();
        public DetectionSettings Detection = new DetectionSettings();
        public bool Enabled = true;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartReticle"); }
        }

        public static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            try
            {
                if (!File.Exists(FilePath)) return cfg;
                var d = Cfg.Parse(File.ReadAllLines(FilePath, Encoding.UTF8));
                cfg.Crosshair.Read(d);
                cfg.Detection.Read(d);
                cfg.Enabled = Cfg.Bool(d, "enabled", true);
            }
            catch (Exception ex)
            {
                Log.Write("Config load failed, using defaults: " + ex.Message);
            }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var d = new SortedDictionary<string, string>();
                Crosshair.Write(d);
                Detection.Write(d);
                d["enabled"] = Enabled ? "1" : "0";
                string tmp = FilePath + ".tmp";
                File.WriteAllLines(tmp, d.Select(kv => kv.Key + "=" + kv.Value), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Write("Config save failed: " + ex.Message);
            }
        }

        /// <summary>Share code: base64 of the crosshair look only (no game list).</summary>
        public static string ToShareCode(CrosshairSettings s)
        {
            var d = new SortedDictionary<string, string>();
            s.Write(d);
            string text = string.Join(";", d.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            return "SR1-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        public static CrosshairSettings FromShareCode(string code)
        {
            if (code == null) return null;
            code = code.Trim();
            if (!code.StartsWith("SR1-", StringComparison.Ordinal)) return null;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(code.Substring(4)));
                var d = Cfg.Parse(text.Split(';'));
                var s = new CrosshairSettings();
                s.Read(d);
                return s;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    /// <summary>Tolerant key=value parsing with clamping, so a hand-edited or corrupt file never crashes the app.</summary>
    internal static class Cfg
    {
        public static Dictionary<string, string> Parse(IEnumerable<string> lines)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        public static int Int(IDictionary<string, string> d, string k, int def, int min, int max)
        {
            string v; int r;
            if (!d.TryGetValue(k, out v) || !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return def;
            return Math.Max(min, Math.Min(max, r));
        }

        public static bool Bool(IDictionary<string, string> d, string k, bool def)
        {
            string v;
            if (!d.TryGetValue(k, out v)) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static string Str(IDictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v.Length > 0 ? v : def;
        }

        public static T Enum<T>(IDictionary<string, string> d, string k, T def) where T : struct
        {
            string v; T r;
            if (d.TryGetValue(k, out v) && System.Enum.TryParse(v, true, out r) && System.Enum.IsDefined(typeof(T), r)) return r;
            return def;
        }

        public static Color Col(IDictionary<string, string> d, string k, Color def)
        {
            string v;
            if (!d.TryGetValue(k, out v)) return def;
            try { return ColorTranslator.FromHtml(v); }
            catch (Exception) { return def; }
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppConfig.Dir);
                    string path = Path.Combine(AppConfig.Dir, "debug.log");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine);
                }
            }
            catch (IOException)
            {
                // Logging must never break the overlay.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
