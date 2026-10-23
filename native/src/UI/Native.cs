// PrismSync native app - small Win32 helpers for the dark window frame + app icon.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PrismSync.UI
{
    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Dark title bar + (on Windows 11) rounded corners and matching caption
        /// colours. Every call is best effort: older Windows just ignores it.
        /// </summary>
        public static void ApplyDarkFrame(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));

                int corner = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));

                int caption = ColorRef(Theme.Bg2);
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));

                int border = ColorRef(Color.FromArgb(30, 36, 47));
                DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));

                int text = ColorRef(Color.FromArgb(236, 240, 246));
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));
            }
            catch
            {
                // No DWM (very old Windows) - the normal caption is still usable.
            }
        }

        private static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }
    }

    /// <summary>Loads the application icon that build.ps1 embeds into the exe.</summary>
    internal static class BrandIcon
    {
        private static Bitmap _cache;
        private static bool _tried;

        public static Bitmap Get(int size)
        {
            if (_tried && _cache != null && _cache.Width >= size) return _cache;
            _tried = true;
            try
            {
                string exe = Application.ExecutablePath;
                using (Icon ico = Icon.ExtractAssociatedIcon(exe))
                {
                    if (ico != null)
                    {
                        using (Icon big = new Icon(ico, size, size))
                        {
                            Bitmap bmp = big.ToBitmap();
                            if (bmp != null && bmp.Width > 0)
                            {
                                if (_cache != null) _cache.Dispose();
                                _cache = bmp;
                                return _cache;
                            }
                        }
                    }
                }
            }
            catch
            {
                // fall through to the vector logo
            }
            if (_cache == null) _cache = DrawFallback(size);
            return _cache;
        }

        /// <summary>Vector fallback: a glowing colour wheel, used when the exe has no icon.</summary>
        private static Bitmap DrawFallback(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p1 = new Pen(Theme.Accent, size * 0.09f))
                    g.DrawArc(p1, size * 0.18f, size * 0.18f, size * 0.64f, size * 0.64f, 150, 250);
                using (Pen p2 = new Pen(Theme.Accent2, size * 0.09f))
                    g.DrawArc(p2, size * 0.18f, size * 0.18f, size * 0.64f, size * 0.64f, 30, 110);
                using (Pen p3 = new Pen(Theme.Good, size * 0.09f))
                    g.DrawArc(p3, size * 0.18f, size * 0.18f, size * 0.64f, size * 0.64f, -80, 100);
            }
            return bmp;
        }
    }
}
