// PrismSync native app - dark palette, cached fonts and GDI+ drawing helpers.
//
// Everything the UI draws goes through this file so the whole window shares one
// visual language: deep navy background, soft rounded cards, one accent gradient.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public static class Theme
    {
        // ------------------------------------------------------------------ fonts
        public static readonly string UiFont = "Microsoft YaHei UI";
        public static readonly string MonoFont = "Consolas";

        public static readonly Font F7_5 = Make(7.5f, FontStyle.Regular);
        public static readonly Font F8 = Make(8f, FontStyle.Regular);
        public static readonly Font F8_5 = Make(8.5f, FontStyle.Regular);
        public static readonly Font F9 = Make(9f, FontStyle.Regular);
        public static readonly Font F9B = Make(9f, FontStyle.Bold);
        public static readonly Font F10 = Make(10f, FontStyle.Regular);
        public static readonly Font F10B = Make(10f, FontStyle.Bold);
        public static readonly Font F11B = Make(11f, FontStyle.Bold);
        public static readonly Font F13B = Make(13f, FontStyle.Bold);
        public static readonly Font F16B = Make(16f, FontStyle.Bold);
        public static readonly Font M8 = new Font(MonoFont, 8f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font M9 = new Font(MonoFont, 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font M10 = new Font(MonoFont, 10f, FontStyle.Regular, GraphicsUnit.Point);

        private static Font Make(float size, FontStyle style)
        {
            return new Font(UiFont, size, style, GraphicsUnit.Point);
        }

        /// <summary>Creates a new font (caller owns it); prefer the cached F* fields.</summary>
        public static Font Font(float size, bool bold)
        {
            return new Font(UiFont, size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        // ----------------------------------------------------------------- palette
        public static readonly Color Bg = Color.FromArgb(10, 12, 17);
        public static readonly Color Bg2 = Color.FromArgb(13, 16, 22);
        public static readonly Color Deep = Color.FromArgb(7, 9, 13);
        public static readonly Color Sidebar = Color.FromArgb(12, 15, 21);
        public static readonly Color Panel = Color.FromArgb(19, 23, 32);
        public static readonly Color Card = Color.FromArgb(24, 29, 39);
        public static readonly Color CardTop = Color.FromArgb(28, 34, 46);
        public static readonly Color CardBottom = Color.FromArgb(20, 24, 33);
        public static readonly Color CardHover = Color.FromArgb(33, 39, 52);
        public static readonly Color Border = Color.FromArgb(40, 47, 61);
        public static readonly Color BorderLight = Color.FromArgb(58, 66, 86);
        public static readonly Color Text = Color.FromArgb(232, 236, 244);
        public static readonly Color TextDim = Color.FromArgb(148, 157, 176);
        public static readonly Color TextFaint = Color.FromArgb(101, 110, 129);
        public static readonly Color Accent = Color.FromArgb(80, 160, 255);
        public static readonly Color Accent2 = Color.FromArgb(166, 96, 255);
        public static readonly Color Good = Color.FromArgb(56, 211, 132);
        public static readonly Color Warn = Color.FromArgb(255, 186, 92);
        public static readonly Color Bad = Color.FromArgb(255, 95, 110);

        public const float CardRadius = 12f;

        // ------------------------------------------------------------------ colors
        public static Color A(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        public static Color Mix(Color a, Color b, double f)
        {
            f = Fx.Clamp01(f);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * f),
                (int)(a.G + (b.G - a.G) * f),
                (int)(a.B + (b.B - a.B) * f));
        }

        public static Color RgbColor(Rgb c)
        {
            return Color.FromArgb(c.R, c.G, c.B);
        }

        /// <summary>An Rgb scaled by 0..1 and lifted by an ambient floor so black still reads.</summary>
        public static Color RgbColor(Rgb c, double scale)
        {
            double f = Fx.Clamp01(scale);
            return Color.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }

        public static double Lum(Rgb c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        public static double Lum(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        // ------------------------------------------------------------------ shapes
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float max = Math.Min(r.Width, r.Height) / 2f;
            if (radius > max) radius = max;
            if (radius <= 0.5f || r.Width <= 1f || r.Height <= 1f)
            {
                p.AddRectangle(r);
                return p;
            }
            float d = radius * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static LinearGradientBrush VBrush(RectangleF r, Color top, Color bottom)
        {
            RectangleF rr = new RectangleF(r.X, r.Y, Math.Max(1f, r.Width), Math.Max(2f, r.Height));
            LinearGradientBrush b = new LinearGradientBrush(rr, top, bottom, LinearGradientMode.Vertical);
            b.WrapMode = WrapMode.TileFlipXY;
            return b;
        }

        private static LinearGradientBrush HBrush(RectangleF r, Color left, Color right)
        {
            RectangleF rr = new RectangleF(r.X, r.Y, Math.Max(2f, r.Width), Math.Max(1f, r.Height));
            LinearGradientBrush b = new LinearGradientBrush(rr, left, right, LinearGradientMode.Horizontal);
            b.WrapMode = WrapMode.TileFlipXY;
            return b;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
        }

        public static void FillVRound(Graphics g, RectangleF r, float radius, Color top, Color bottom)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (LinearGradientBrush b = VBrush(r, top, bottom)) g.FillPath(b, p);
        }

        public static void FillHRound(Graphics g, RectangleF r, float radius, Color left, Color right)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (LinearGradientBrush b = HBrush(r, left, right)) g.FillPath(b, p);
        }

        public static void FillV(Graphics g, RectangleF r, Color top, Color bottom)
        {
            using (LinearGradientBrush b = VBrush(r, top, bottom)) g.FillRectangle(b, r);
        }

        public static void FillH(Graphics g, RectangleF r, Color left, Color right)
        {
            using (LinearGradientBrush b = HBrush(r, left, right)) g.FillRectangle(b, r);
        }

        public static void DrawRound(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            using (GraphicsPath p = Rounded(r, radius))
            using (Pen pen = new Pen(color, width)) g.DrawPath(pen, p);
        }

        public static void HLine(Graphics g, float x1, float x2, float y, Color c)
        {
            using (Pen pen = new Pen(c, 1f)) g.DrawLine(pen, x1, y, x2, y);
        }

        public static void VLine(Graphics g, float x, float y1, float y2, Color c)
        {
            using (Pen pen = new Pen(c, 1f)) g.DrawLine(pen, x, y1, x, y2);
        }

        /// <summary>A raised card: vertical gradient, 1px border, faint top inner light.</summary>
        public static void DrawCard(Graphics g, RectangleF r)
        {
            DrawCard(g, r, CardRadius);
        }

        public static void DrawCard(Graphics g, RectangleF r, float radius)
        {
            using (GraphicsPath p = Rounded(r, radius))
            {
                using (LinearGradientBrush b = VBrush(r, CardTop, CardBottom)) g.FillPath(b, p);
                using (Pen pen = new Pen(A(Border, 215), 1f)) g.DrawPath(pen, p);
            }
            using (Pen pen = new Pen(A(Color.White, 8), 1f))
                g.DrawLine(pen, r.X + radius, r.Y + 1f, r.Right - radius, r.Y + 1f);
        }

        /// <summary>Rounded soft shadow used under floating buttons.</summary>
        public static void Shadow(Graphics g, RectangleF r, float radius, int alpha)
        {
            for (int i = 3; i >= 1; i--)
            {
                RectangleF rr = new RectangleF(r.X - i, r.Y + i * 0.8f, r.Width + i * 2f, r.Height + i * 1.4f);
                int a = (int)(alpha * (1.0 - i * 0.28));
                if (a <= 0) continue;
                FillRound(g, rr, radius + i, A(Color.Black, a));
            }
        }

        /// <summary>Radial glow: colour fading to transparent, centred on the rect.</summary>
        public static void Glow(Graphics g, RectangleF area, Color color, double strength)
        {
            if (strength <= 0.01) return;
            int steps = 8;
            for (int i = steps; i >= 1; i--)
            {
                float t = i / (float)steps;
                int alpha = (int)(strength * 86 * (1 - t) * (1 - t));
                if (alpha <= 0) continue;
                float w = area.Width * t;
                float h = area.Height * t;
                RectangleF r = new RectangleF(area.X + (area.Width - w) / 2f,
                                              area.Y + (area.Height - h) / 2f, w, h);
                using (SolidBrush b = new SolidBrush(A(color, alpha))) g.FillEllipse(b, r);
            }
        }

        public static void Dot(Graphics g, float cx, float cy, float radius, Color color)
        {
            using (SolidBrush b = new SolidBrush(color))
                g.FillEllipse(b, cx - radius, cy - radius, radius * 2f, radius * 2f);
        }

        /// <summary>Status dot with a soft halo (used for online / offline devices).</summary>
        public static void StatusDot(Graphics g, float cx, float cy, float radius, Color color, bool glow)
        {
            if (glow) Glow(g, new RectangleF(cx - radius * 3.2f, cy - radius * 3.2f, radius * 6.4f, radius * 6.4f), color, 0.55);
            Dot(g, cx, cy, radius, color);
        }

        // -------------------------------------------------------------------- text
        public static readonly TextFormatFlags Left =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags Center =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags Right =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags LeftTop =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags CenterTop =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags RightTop =
            TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        public static readonly TextFormatFlags WrapLeft =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        public static readonly TextFormatFlags EllipsisLeft =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        public static readonly TextFormatFlags EllipsisRight =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        public static void Label(Graphics g, string text, Font font, Color color, RectangleF rect, TextFormatFlags flags)
        {
            if (text == null) return;
            TextRenderer.DrawText(g, text, font, Rectangle.Round(rect), color, flags);
        }

        public static void Label(Graphics g, string text, Font font, Color color, float x, float y)
        {
            if (text == null) return;
            TextRenderer.DrawText(g, text, font, new Point((int)x, (int)y), color);
        }

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                                            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        public static Size Measure(string text, Font font, int maxWidth)
        {
            return TextRenderer.MeasureText(text, font, new Size(maxWidth, int.MaxValue),
                                            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}
