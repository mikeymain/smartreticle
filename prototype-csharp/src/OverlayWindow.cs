using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartXair
{
    /// <summary>
    /// Click-through, never-focused, per-pixel-alpha window that holds the crosshair bitmap.
    /// NOACTIVATE matters: if the overlay could take focus, the detector would think you tabbed out.
    /// </summary>
    internal sealed class OverlayWindow : Form
    {
        private Bitmap bitmap;
        private int halfSize;
        private byte opacity = 255;
        private Point lastCenter = new Point(int.MinValue, int.MinValue);
        private bool lastVisible;
        private bool dirty = true;
        private DateTime lastTopmost = DateTime.MinValue;

        public OverlayWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "SmartXair Overlay";
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW |
                              Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void SetCrosshair(CrosshairSettings s)
        {
            var next = CrosshairRenderer.Render(s);
            if (bitmap != null) bitmap.Dispose();
            bitmap = next;
            halfSize = bitmap.Width / 2;
            opacity = (byte)Math.Max(1, Math.Min(255, s.Opacity * 255 / 100));
            dirty = true;
        }

        /// <summary>Cheap to call every tick: only pushes to the compositor when something changed.</summary>
        public void Place(Point center, bool visible)
        {
            if (bitmap == null) return;
            if (!dirty && visible == lastVisible && (!visible || center == lastCenter))
            {
                KeepOnTop(visible);
                return;
            }
            Push(center, visible ? opacity : (byte)0);
            lastCenter = center;
            lastVisible = visible;
            dirty = false;
            KeepOnTop(visible);
        }

        private void KeepOnTop(bool visible)
        {
            // Borderless games can be topmost too; re-assert about once a second while shown.
            if (!visible || (DateTime.UtcNow - lastTopmost).TotalMilliseconds < 1000) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            lastTopmost = DateTime.UtcNow;
        }

        private void Push(Point center, byte alpha)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr old = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                old = Native.SelectObject(memDc, hBitmap);
                var size = new Native.SIZE(bitmap.Width, bitmap.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT(center.X - halfSize, center.Y - halfSize);
                var blend = new Native.BLENDFUNCTION
                {
                    BlendOp = Native.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = alpha,
                    AlphaFormat = Native.AC_SRC_ALPHA
                };
                if (!Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
                    Log.Write("UpdateLayeredWindow failed, error " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
            finally
            {
                if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
                Native.DeleteDC(memDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bitmap != null)
            {
                bitmap.Dispose();
                bitmap = null;
            }
            base.Dispose(disposing);
        }
    }
}
