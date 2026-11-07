using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SmartXair
{
    /// <summary>Draws a crosshair into a square ARGB bitmap whose centre pixel is the aim point.</summary>
    public static class CrosshairRenderer
    {
        public static Bitmap Render(CrosshairSettings s)
        {
            int e = Extent(s);
            int size = e * 2 + 1;
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                int o = s.Outline ? s.OutlineThickness : 0;
                if (o > 0) DrawShape(g, s, e, o, s.OutlineColor);
                DrawShape(g, s, e, 0, s.Color);
            }
            return bmp;
        }

        /// <summary>Half-size of the bitmap: farthest pixel any part can reach from the centre.</summary>
        public static int Extent(CrosshairSettings s)
        {
            int arms = s.Gap + s.Length + s.Thickness;
            int circle = s.CircleRadius + s.Thickness;
            int ext = Math.Max(Math.Max(arms, circle), s.DotSize);
            return ext + s.OutlineThickness + 2;
        }

        private static bool HasArms(CrosshairStyle st)
        {
            return st == CrosshairStyle.Cross || st == CrosshairStyle.CrossDot || st == CrosshairStyle.TShape || st == CrosshairStyle.CircleCross;
        }

        private static bool HasDot(CrosshairStyle st)
        {
            return st == CrosshairStyle.Dot || st == CrosshairStyle.CrossDot || st == CrosshairStyle.CircleDot;
        }

        private static bool HasCircle(CrosshairStyle st)
        {
            return st == CrosshairStyle.Circle || st == CrosshairStyle.CircleDot || st == CrosshairStyle.CircleCross;
        }

        /// <param name="grow">0 for the fill pass; outline thickness for the outline pass (shapes expand by it).</param>
        private static void DrawShape(Graphics g, CrosshairSettings s, int c, int grow, Color color)
        {
            using (var brush = new SolidBrush(color))
            {
                if (HasArms(s.Style) && s.Length > 0) DrawArms(g, brush, s, c, grow);
                if (HasDot(s.Style)) DrawDot(g, brush, s.DotSize, c, grow);
                if (HasCircle(s.Style)) DrawCircle(g, color, s, c, grow);
            }
        }

        private static void DrawArms(Graphics g, Brush b, CrosshairSettings s, int c, int grow)
        {
            g.SmoothingMode = SmoothingMode.None;
            int t = s.Thickness;
            int start = c - t / 2;        // first pixel of the centre block
            int near = start - s.Gap;     // inner end of the left/top arms
            int far = start + t + s.Gap;  // inner start of the right/bottom arms
            int L = s.Length;

            Fill(g, b, far, start, L, t, grow);            // right
            Fill(g, b, near - L, start, L, t, grow);       // left
            Fill(g, b, start, far, t, L, grow);            // bottom
            if (s.Style != CrosshairStyle.TShape) Fill(g, b, start, near - L, t, L, grow); // top
            if (s.Gap == 0) Fill(g, b, start, start, t, t, grow);
        }

        private static void DrawDot(Graphics g, Brush b, int d, int c, int grow)
        {
            int x = c - d / 2;
            if (d >= 4)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, x - grow, x - grow, d + 2 * grow, d + 2 * grow);
            }
            else
            {
                g.SmoothingMode = SmoothingMode.None;
                Fill(g, b, x, x, d, d, grow);
            }
        }

        private static void DrawCircle(Graphics g, Color color, CrosshairSettings s, int c, int grow)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float center = c + 0.5f;
            float r = s.CircleRadius;
            using (var pen = new Pen(color, s.Thickness + 2 * grow))
            {
                g.DrawEllipse(pen, center - r, center - r, 2 * r, 2 * r);
            }
        }

        private static void Fill(Graphics g, Brush b, int x, int y, int w, int h, int grow)
        {
            g.FillRectangle(b, x - grow, y - grow, w + 2 * grow, h + 2 * grow);
        }
    }
}
