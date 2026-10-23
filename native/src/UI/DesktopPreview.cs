// PrismSync native app - live desktop preview: headset, laptop and the full
// keyboard rendered with the colours of the frame the engine just produced.
//
// The keyboard is drawn entirely from KeyLayout (Count / Row / Width), so a
// 68-key board would render correctly without touching this file.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public sealed class DesktopPreview : Control
    {
        private readonly PreviewData _empty = new PreviewData();
        private AppSnapshot _snap;
        private KeyLayout _layout;

        private KeyLayout _cachedLayout;
        private List<int>[] _rows;
        private double[] _rowWidths;

        public DesktopPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            TabStop = false;
        }

        public void Tick(AppSnapshot snap, KeyLayout layout)
        {
            _snap = snap;
            _layout = layout;
            Invalidate();
        }

        // ------------------------------------------------------------------ paint
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            RectangleF full = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.DrawCard(g, full, Theme.CardRadius);

            PaintHeader(g);

            RectangleF stage = new RectangleF(14f, 40f, Width - 28f, Height - 52f);
            if (stage.Width < 30f || stage.Height < 34f) return;
            PaintStage(g, stage);
        }

        private PreviewData Data
        {
            get
            {
                if (_snap != null && _snap.Preview != null) return _snap.Preview;
                return _empty;
            }
        }

        private DeviceRuntimeState FindDevice(string id)
        {
            if (_snap == null || _snap.Devices == null) return null;
            for (int i = 0; i < _snap.Devices.Length; i++)
                if (_snap.Devices[i] != null && _snap.Devices[i].Id == id) return _snap.Devices[i];
            return null;
        }

        private Color DeviceColor(string id, Color lit)
        {
            DeviceRuntimeState d = FindDevice(id);
            if (d != null && !d.Enabled) return Theme.A(Theme.TextFaint, 150);
            return lit;
        }

        private void PaintHeader(Graphics g)
        {
            Theme.Label(g, "桌面预览", Theme.F10B, Theme.Text,
                       new RectangleF(16f, 11f, 140f, 24f), Theme.Left);

            PreviewData p = Data;
            string[] ids = new string[] { "headset", "laptop", "keyboard" };
            string[] names = new string[] { "耳机", "笔记本", "键盘" };
            Color[] colors = new Color[3];
            colors[0] = DeviceColor("headset", Theme.Mix(Theme.RgbColor(p.Headset), Color.White, 0.20));
            colors[1] = DeviceColor("laptop", Theme.Mix(Theme.RgbColor(p.Laptop), Color.White, 0.20));
            colors[2] = DeviceColor("keyboard", Theme.Mix(Theme.RgbColor(AverageKeys(p.Keyboard)), Color.White, 0.20));

            float yMid = 23f;
            float x = Width - 16f;
            for (int i = ids.Length - 1; i >= 0; i--)
            {
                Size ts = Theme.Measure(names[i], Theme.F8_5);
                x -= ts.Width;
                Theme.Label(g, names[i], Theme.F8_5, Theme.TextDim,
                           new RectangleF(x, yMid - 9f, ts.Width + 2f, 18f), Theme.Left);
                x -= 11f;
                Theme.Dot(g, x - 1f, yMid, 2.8f, colors[i]);
                x -= 13f;
            }
        }

        private void PaintStage(Graphics g, RectangleF stage)
        {
            PreviewData p = Data;
            Rgb avg = AverageColor(p);
            double lum = Theme.Lum(avg);
            Color avgC = Theme.RgbColor(avg);

            Theme.FillVRound(g, stage, 10f, Color.FromArgb(7, 9, 14), Color.FromArgb(17, 21, 30));
            Theme.DrawRound(g, stage, 10f, Theme.A(Theme.Border, 150), 1f);

            RectangleF inner = new RectangleF(stage.X + 1f, stage.Y + 1f, stage.Width - 2.5f, stage.Height - 2.5f);
            using (GraphicsPath clip = Theme.Rounded(inner, 9f))
            {
                Region saved = g.Clip;
                try
                {
                    g.SetClip(clip, CombineMode.Replace);
                    PaintScene(g, stage, p, avgC, lum);
                }
                finally
                {
                    g.Clip = saved;
                    saved.Dispose();
                }
            }
        }

        private void PaintScene(Graphics g, RectangleF stage, PreviewData p, Color avgC, double lum)
        {
            float regionW = Math.Min(stage.Width, 980f);
            float regionX = stage.X + (stage.Width - regionW) / 2f;
            float deskY = stage.Bottom - Math.Max(12f, stage.Height * 0.12f);

            // ambient light of the whole desk
            RectangleF amb = new RectangleF(regionX + regionW * 0.10f, stage.Y + stage.Height * 0.14f,
                                            regionW * 0.80f, stage.Height * 0.92f);
            Theme.Glow(g, amb, avgC, 0.20 + lum * 0.58);

            // glow spilling onto the desk
            float floorH = Math.Max(4f, stage.Bottom - deskY + 4f);
            RectangleF floor = new RectangleF(regionX + 8f, deskY - 3f, Math.Max(2f, regionW - 16f), floorH);
            using (LinearGradientBrush b = new LinearGradientBrush(
                       new RectangleF(floor.X, floor.Y, floor.Width, Math.Max(2f, floor.Height)),
                       Theme.A(avgC, (int)(22 + 78 * lum)), Theme.A(avgC, 0), LinearGradientMode.Vertical))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillRectangle(b, floor);
            }
            Theme.HLine(g, regionX + 12f, regionX + regionW - 12f, deskY,
                        Theme.A(avgC, (int)(16 + 52 * lum)));

            // ---- device geometry (all bottom edges sit on the desk line) ----
            float hsW = Math.Min(regionW * 0.105f, 62f);
            float hsH = hsW * 1.55f;
            if (hsH > stage.Height * 0.62f)
            {
                hsH = stage.Height * 0.62f;
                hsW = hsH / 1.55f;
            }
            RectangleF hsArea = new RectangleF(regionX + regionW * 0.115f - hsW / 2f, deskY - hsH, hsW, hsH);

            float lapW = Math.Min(regionW * 0.225f, 210f);
            float lapScreenH = lapW * 0.60f;
            float lapBaseH = Math.Max(5f, lapScreenH * 0.10f);
            float lapTotal = lapScreenH + lapBaseH;
            if (lapTotal > stage.Height * 0.63f)
            {
                float f = (stage.Height * 0.63f) / lapTotal;
                lapW *= f;
                lapTotal = stage.Height * 0.63f;
            }
            RectangleF lapArea = new RectangleF(regionX + regionW * 0.42f - lapW / 2f, deskY - lapTotal, lapW, lapTotal);

            float kbW = Math.Min(regionW * 0.42f, 470f);
            float kbH = kbW * 0.32f;
            if (kbH > stage.Height * 0.62f)
            {
                kbH = stage.Height * 0.62f;
                kbW = kbH / 0.32f;
            }
            RectangleF kbArea = new RectangleF(regionX + regionW * 0.735f - kbW / 2f, deskY - kbH, kbW, kbH);

            PaintLaptop(g, lapArea, p.Laptop);
            PaintHeadset(g, hsArea, p.Headset);
            PaintKeyboard(g, kbArea, p.Keyboard);
        }

        // ---------------------------------------------------------------- laptop
        private void PaintLaptop(Graphics g, RectangleF area, Rgb color)
        {
            if (area.Width < 8f) return;
            float baseH = Math.Max(4.5f, area.Height * 0.10f);
            float screenH = area.Height - baseH;
            if (screenH < 4f) return;

            double lum = Theme.Lum(color);
            Color cc = Theme.RgbColor(color);

            Theme.Glow(g, new RectangleF(area.X - area.Width * 0.20f, area.Y - screenH * 0.16f,
                                          area.Width * 1.40f, area.Height * 1.28f), cc, 0.14 + 0.62 * lum);

            RectangleF screen = new RectangleF(area.X, area.Y, area.Width, screenH);
            RectangleF baseR = new RectangleF(area.X - area.Width * 0.055f, area.Y + screenH,
                                              area.Width * 1.11f, baseH);

            // shadow under the machine
            using (SolidBrush sh = new SolidBrush(Theme.A(Color.Black, 95)))
                g.FillEllipse(sh, area.X - area.Width * 0.10f, area.Bottom - 2.5f,
                              area.Width * 1.20f, 5.5f);

            // base (trapezoid, wider at the front)
            using (GraphicsPath bp = new GraphicsPath())
            {
                bp.AddPolygon(new PointF[]
                {
                    new PointF(baseR.Left, baseR.Bottom),
                    new PointF(baseR.Right, baseR.Bottom),
                    new PointF(screen.Right, baseR.Top),
                    new PointF(screen.Left, baseR.Top)
                });
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new RectangleF(baseR.X, baseR.Y, Math.Max(2f, baseR.Width), Math.Max(2f, baseR.Height)),
                           Color.FromArgb(46, 53, 68), Color.FromArgb(18, 22, 30), LinearGradientMode.Vertical))
                {
                    b.WrapMode = WrapMode.TileFlipXY;
                    g.FillPath(b, bp);
                }
                using (Pen pen = new Pen(Theme.A(Theme.BorderLight, 120), 1f)) g.DrawPath(pen, bp);
            }
            Theme.HLine(g, baseR.Left + 3f, baseR.Right - 3f, baseR.Bottom - 1f, Theme.A(cc, 70));

            // lid / bezel
            RectangleF lid = new RectangleF(screen.X - 3f, screen.Y - 2.5f, screen.Width + 6f, screen.Height + 4f);
            Theme.FillVRound(g, lid, 4f, Color.FromArgb(34, 40, 52), Color.FromArgb(14, 17, 23));
            Theme.DrawRound(g, lid, 4f, Theme.A(Theme.BorderLight, 120), 1f);

            // display
            RectangleF disp = new RectangleF(screen.X + 1f, screen.Y + 1f, screen.Width - 2f, screen.Height - 2.5f);
            if (disp.Width < 4f || disp.Height < 3f) return;
            using (GraphicsPath dp = Theme.Rounded(disp, 2.4f))
            {
                Color dTop = Theme.Mix(Color.FromArgb(6, 8, 12), cc, 0.24 + 0.62 * lum);
                Color dBot = Theme.Mix(Color.FromArgb(4, 6, 9), cc, 0.08 + 0.34 * lum);
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new RectangleF(disp.X, disp.Y, Math.Max(2f, disp.Width), Math.Max(2f, disp.Height)),
                           dTop, dBot, LinearGradientMode.Vertical))
                {
                    b.WrapMode = WrapMode.TileFlipXY;
                    g.FillPath(b, dp);
                }

                Region saved = g.Clip;
                try
                {
                    g.SetClip(dp, CombineMode.Intersect);
                    // soft diagonal glass reflection
                    using (LinearGradientBrush gl = new LinearGradientBrush(
                               new RectangleF(disp.X - disp.Width, disp.Y - disp.Height,
                                              Math.Max(2f, disp.Width * 2f), Math.Max(2f, disp.Height * 2f)),
                               Theme.A(Color.White, 30), Theme.A(Color.White, 0), 40f))
                    {
                        gl.WrapMode = WrapMode.TileFlipXY;
                        g.FillRectangle(gl, disp);
                    }
                    Theme.HLine(g, disp.X, disp.Right, disp.Bottom - Math.Max(3f, disp.Height * 0.16f),
                                Theme.A(Color.White, 14));
                }
                finally
                {
                    g.Clip = saved;
                    saved.Dispose();
                }
                using (Pen pen = new Pen(Theme.A(Theme.Mix(cc, Color.White, 0.25), 90), 1f))
                    g.DrawPath(pen, dp);
            }
        }

        // --------------------------------------------------------------- headset
        private void PaintHeadset(Graphics g, RectangleF area, Rgb color)
        {
            float w = area.Width;
            float h = area.Height;
            if (w < 6f || h < 8f) return;

            double lum = Theme.Lum(color);
            Color cc = Theme.RgbColor(color);
            float cx = area.X + w / 2f;
            float top = area.Y;
            float bottom = area.Bottom;

            Theme.Glow(g, new RectangleF(area.X - w * 0.38f, area.Y - h * 0.10f,
                                          w * 1.76f, h * 1.20f), cc, 0.14 + 0.60 * lum);

            using (SolidBrush sh = new SolidBrush(Theme.A(Color.Black, 95)))
                g.FillEllipse(sh, cx - w * 0.62f, bottom - 2.5f, w * 1.24f, 5.2f);

            float cupW = Math.Max(8f, w * 0.30f);
            float cupH = Math.Max(14f, h * 0.36f);
            float yMid = bottom - cupH * 0.5f - Math.Max(2f, h * 0.03f);
            float bandTh = Math.Max(3.4f, w * 0.145f);
            float bandW = Math.Max(4f, w - bandTh);
            RectangleF bandRect = new RectangleF(cx - bandW / 2f, top, bandW, Math.Max(4f, (yMid - top) * 2f));

            using (Pen pen = new Pen(Color.FromArgb(42, 50, 64), bandTh))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, bandRect, 180f, 180f);
            }
            using (Pen pen = new Pen(Theme.A(Theme.Mix(cc, Color.White, 0.35), 120), Math.Max(1f, bandTh * 0.30f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, bandRect, 180f, 180f);
            }

            float cupR = cupW * 0.42f;
            int ringAlpha = (int)(70 + 150 * Math.Min(1.0, lum * 2.4));
            DrawEarCup(g, bandRect.X - cupW / 2f, yMid - cupH / 2f, cupW, cupH, cupR, cc, ringAlpha);
            DrawEarCup(g, bandRect.Right - cupW / 2f, yMid - cupH / 2f, cupW, cupH, cupR, cc, ringAlpha);
        }

        private void DrawEarCup(Graphics g, float x, float y, float w, float h, float radius, Color cc, int ringAlpha)
        {
            RectangleF cup = new RectangleF(x, y, w, h);
            Theme.FillVRound(g, cup, radius, Color.FromArgb(38, 45, 58), Color.FromArgb(16, 20, 27));
            Theme.DrawRound(g, cup, radius, Theme.A(Theme.BorderLight, 130), 1f);
            RectangleF ring = RectangleF.Inflate(cup, -w * 0.16f, -h * 0.11f);
            Theme.DrawRound(g, ring, radius * 0.78f, Theme.A(cc, ringAlpha), Math.Max(1f, w * 0.11f));
        }

        // -------------------------------------------------------------- keyboard
        private void PaintKeyboard(Graphics g, RectangleF area, Rgb[] keys)
        {
            float w = area.Width;
            float h = area.Height;
            if (w < 10f || h < 8f || _layout == null) return;

            EnsureRows(_layout);
            int count = _layout.LedId.Length;
            if (keys == null) return;
            if (count > keys.Length) count = keys.Length;
            if (_rows == null) return;

            Rgb avgKey = AverageKeys(keys);
            double lum = Theme.Lum(avgKey);
            Color cA = Theme.RgbColor(avgKey);

            Theme.Glow(g, new RectangleF(area.X - w * 0.10f, area.Y + h * 0.05f,
                                          w * 1.20f, h * 1.02f), cA, 0.18 + 0.66 * lum);

            using (SolidBrush sh = new SolidBrush(Theme.A(Color.Black, 105)))
                g.FillEllipse(sh, area.X - w * 0.03f, area.Bottom - 2.5f, w * 1.06f, 5.4f);

            Theme.FillVRound(g, area, 5f, Color.FromArgb(32, 38, 50), Color.FromArgb(15, 18, 25));
            Theme.DrawRound(g, area, 5f, Theme.A(Theme.BorderLight, 125), 1f);

            float padX = Math.Max(3f, w * 0.013f);
            float padY = Math.Max(2.4f, h * 0.040f);
            RectangleF plate = RectangleF.Inflate(area, -padX, -padY);
            Theme.FillVRound(g, plate, 3.5f, Color.FromArgb(11, 13, 18), Color.FromArgb(9, 11, 15));

            int rowCount = _rows.Length;
            if (rowCount <= 0) return;
            float gap = Math.Max(0.9f, w * 0.0032f);
            float rowH = (plate.Height - gap * (rowCount + 1)) / rowCount;
            if (rowH < 2f) return;

            for (int r = 0; r < rowCount; r++)
            {
                List<int> idx = _rows[r];
                if (idx == null || idx.Count == 0) continue;
                double rowSum = _rowWidths[r];
                if (rowSum <= 0.0001) continue;

                float unit = plate.Width / (float)rowSum;
                float y = plate.Y + gap + r * (rowH + gap);
                float x = plate.X + gap * 0.5f;
                float keyH = rowH - gap;

                for (int j = 0; j < idx.Count; j++)
                {
                    int i = idx[j];
                    float keyW = (float)(_layout.Width[i] * unit) - gap;
                    if (keyW < 2f) keyW = 2f;

                    Rgb c = keys[i];
                    double kl = Theme.Lum(c);
                    Color fill = Theme.Mix(Color.FromArgb(34, 40, 53), Theme.RgbColor(c), 0.24 + 0.58 * kl);
                    RectangleF key = new RectangleF(x, y, keyW, keyH);
                    Theme.FillRound(g, key, 2.2f, fill);

                    if (keyW > 4f)
                    {
                        using (Pen pen = new Pen(Theme.A(Color.White, 15), 1f))
                            g.DrawLine(pen, x + 2f, y + 1f, x + keyW - 2f, y + 1f);
                        if (kl > 0.02)
                        {
                            using (Pen pen = new Pen(Theme.A(Theme.RgbColor(c), 150), 1f))
                                g.DrawLine(pen, x + 2f, y + keyH - 1f, x + keyW - 2f, y + keyH - 1f);
                        }
                    }
                    x += (float)(_layout.Width[i] * unit);
                }
            }

            // front LED strip under the board
            Theme.HLine(g, area.X + 4f, area.Right - 4f, area.Bottom - 0.5f,
                        Theme.A(cA, (int)(40 + 150 * lum)));
        }

        // ---------------------------------------------------------------- shared
        private void EnsureRows(KeyLayout layout)
        {
            if (layout == null) return;
            if (_cachedLayout == layout && _rows != null) return;
            _cachedLayout = layout;

            int rowCount = layout.RowCount;
            if (rowCount < 1) rowCount = 1;
            List<int>[] rows = new List<int>[rowCount];
            for (int r = 0; r < rowCount; r++) rows[r] = new List<int>();

            int count = layout.LedId.Length;
            for (int i = 0; i < count; i++)
            {
                int r = layout.Row[i];
                if (r < 0) r = 0;
                if (r >= rowCount) r = rowCount - 1;
                rows[r].Add(i);
            }

            double[] widths = new double[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                double sum = 0;
                for (int j = 0; j < rows[r].Count; j++)
                {
                    double kw = layout.Width[rows[r][j]];
                    sum += kw > 0 ? kw : 1.0;
                }
                widths[r] = sum;
            }

            _rows = rows;
            _rowWidths = widths;
        }

        private static Rgb AverageKeys(Rgb[] keys)
        {
            if (keys == null || keys.Length == 0) return Rgb.Black;
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                r += keys[i].R; g += keys[i].G; b += keys[i].B;
            }
            int n = keys.Length;
            return new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }

        private static Rgb AverageColor(PreviewData p)
        {
            double r = 0, g = 0, b = 0;
            int n = 0;
            if (p.Keyboard != null)
            {
                for (int i = 0; i < p.Keyboard.Length; i++)
                {
                    r += p.Keyboard[i].R; g += p.Keyboard[i].G; b += p.Keyboard[i].B; n++;
                }
            }
            r += p.Laptop.R; g += p.Laptop.G; b += p.Laptop.B; n++;
            r += p.Headset.R; g += p.Headset.G; b += p.Headset.B; n++;
            if (n == 0) return Rgb.Black;
            return new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }
    }
}
