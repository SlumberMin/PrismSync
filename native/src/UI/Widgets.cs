// PrismSync native app - small hand-drawn widgets used by the main window:
// buttons, slider, toggle switch, colour swatch, log panel and the dark
// dropdown menu used for the headset firmware modes.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public enum ButtonKind
    {
        Primary,
        Secondary,
        Ghost
    }

    public enum ButtonIcon
    {
        None,
        Play,
        Pause,
        Refresh,
        Shield
    }

    // ------------------------------------------------------------------ button
    public sealed class FlatButton : Control
    {
        private string _caption = "";
        private ButtonKind _kind = ButtonKind.Secondary;
        private ButtonIcon _icon = ButtonIcon.None;
        private bool _small;
        private bool _hover;
        private bool _pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.StandardClick, false);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value != null ? value : ""; Invalidate(); }
        }

        public ButtonKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        public ButtonIcon Icon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool Small
        {
            get { return _small; }
            set { _small = value; Invalidate(); }
        }

        private Font CaptionFont
        {
            get
            {
                if (_small) return Theme.F8_5;
                if (_kind == ButtonKind.Primary) return Theme.F9B;
                return Theme.F9;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool wasPressed = _pressed;
            _pressed = false;
            Invalidate();
            if (wasPressed && ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Font font = CaptionFont;
            Size textSize = Theme.Measure(_caption, font);
            float iconW = _icon == ButtonIcon.None ? 0f : (_small ? 12f : 13f);
            float total = iconW + (_icon == ButtonIcon.None ? 0f : 6f) + textSize.Width;
            float x = (Width - total) / 2f;
            float yOff = _pressed ? 0.5f : 0f;

            Color fg;
            if (_kind == ButtonKind.Primary)
            {
                if (!Enabled)
                {
                    Theme.FillVRound(g, r, 8f, Color.FromArgb(30, 36, 47), Color.FromArgb(22, 27, 36));
                    Theme.DrawRound(g, r, 8f, Theme.Border, 1f);
                    fg = Theme.TextFaint;
                }
                else
                {
                    Theme.Shadow(g, r, 8f, _pressed ? 40 : 80);
                    Color c1 = Theme.Accent;
                    Color c2 = Theme.Accent2;
                    if (_hover)
                    {
                        c1 = Theme.Mix(c1, Color.White, 0.10);
                        c2 = Theme.Mix(c2, Color.White, 0.08);
                    }
                    if (_pressed)
                    {
                        c1 = Theme.Mix(c1, Color.Black, 0.10);
                        c2 = Theme.Mix(c2, Color.Black, 0.10);
                    }
                    Theme.FillHRound(g, r, 8f, c1, c2);
                    Theme.DrawRound(g, r, 8f, Theme.A(Color.White, 34), 1f);
                    Theme.HLine(g, r.X + 10f, r.Right - 10f, r.Y + 1f, Theme.A(Color.White, 46));
                    fg = Color.White;
                }
            }
            else if (_kind == ButtonKind.Secondary)
            {
                Color top = _hover ? Theme.CardHover : Theme.CardTop;
                Color bottom = _hover ? Theme.Card : Theme.CardBottom;
                if (_pressed) { top = Theme.CardBottom; bottom = Theme.Deep; }
                Theme.FillVRound(g, r, 8f, top, bottom);
                Theme.DrawRound(g, r, 8f, _hover ? Theme.A(Theme.BorderLight, 200) : Theme.A(Theme.Border, 235), 1f);
                fg = Enabled ? Theme.Text : Theme.TextFaint;
            }
            else
            {
                if (_hover) Theme.FillRound(g, r, 8f, Theme.A(Color.White, 8));
                fg = Enabled ? Theme.TextDim : Theme.TextFaint;
            }

            if (_icon != ButtonIcon.None)
            {
                RectangleF ir = new RectangleF(x, (Height - iconW) / 2f + yOff, iconW, iconW);
                DrawIcon(g, _icon, ir, fg);
                x += iconW + 6f;
            }
            Theme.Label(g, _caption, font, fg, new RectangleF(x, yOff, textSize.Width + 6f, Height), Theme.Left);
        }

        private static void DrawIcon(Graphics g, ButtonIcon icon, RectangleF r, Color color)
        {
            switch (icon)
            {
                case ButtonIcon.Play:
                {
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddPolygon(new PointF[]
                        {
                            new PointF(r.X + r.Width * 0.18f, r.Y + r.Height * 0.10f),
                            new PointF(r.Right - r.Width * 0.06f, r.Y + r.Height * 0.50f),
                            new PointF(r.X + r.Width * 0.18f, r.Bottom - r.Height * 0.10f)
                        });
                        using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
                    }
                    break;
                }
                case ButtonIcon.Pause:
                {
                    float bw = r.Width * 0.24f;
                    Theme.FillRound(g, new RectangleF(r.X + r.Width * 0.16f, r.Y + r.Height * 0.12f, bw, r.Height * 0.76f), 1.2f, color);
                    Theme.FillRound(g, new RectangleF(r.Right - r.Width * 0.16f - bw, r.Y + r.Height * 0.12f, bw, r.Height * 0.76f), 1.2f, color);
                    break;
                }
                case ButtonIcon.Refresh:
                {
                    using (Pen pen = new Pen(color, Math.Max(1.2f, r.Width * 0.14f)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawArc(pen, r.X + r.Width * 0.12f, r.Y + r.Height * 0.12f,
                                  r.Width * 0.76f, r.Height * 0.76f, 40f, 280f);
                    }
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddPolygon(new PointF[]
                        {
                            new PointF(r.X + r.Width * 0.82f, r.Y + r.Height * 0.08f),
                            new PointF(r.X + r.Width * 0.98f, r.Y + r.Height * 0.24f),
                            new PointF(r.X + r.Width * 0.70f, r.Y + r.Height * 0.28f)
                        });
                        using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
                    }
                    break;
                }
                case ButtonIcon.Shield:
                {
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddPolygon(new PointF[]
                        {
                            new PointF(r.X + r.Width * 0.50f, r.Y + r.Height * 0.06f),
                            new PointF(r.X + r.Width * 0.92f, r.Y + r.Height * 0.22f),
                            new PointF(r.X + r.Width * 0.84f, r.Y + r.Height * 0.68f),
                            new PointF(r.X + r.Width * 0.50f, r.Y + r.Height * 0.94f),
                            new PointF(r.X + r.Width * 0.16f, r.Y + r.Height * 0.68f),
                            new PointF(r.X + r.Width * 0.08f, r.Y + r.Height * 0.22f)
                        });
                        using (SolidBrush b = new SolidBrush(color)) g.FillPath(b, p);
                    }
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ toggle
    public sealed class ToggleSwitch : Control
    {
        private string _caption = "";
        private bool _checked;
        private bool _hover;

        public event Action<bool> Toggled;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.StandardClick, false);
            BackColor = Theme.Card;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value != null ? value : ""; Invalidate(); }
        }

        public bool Checked
        {
            get { return _checked; }
        }

        public void SetChecked(bool value)
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            _checked = !_checked;
            Invalidate();
            Action<bool> h = Toggled;
            if (h != null) h(_checked);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float tw = 34f;
            float th = 18f;
            float tx = Width - tw;
            float ty = (Height - th) / 2f;
            RectangleF track = new RectangleF(tx, ty, tw, th);

            if (_caption.Length > 0)
            {
                Font f = Theme.F8_5;
                Color tc = _checked ? Theme.Text : Theme.TextDim;
                Theme.Label(g, _caption, f, tc, new RectangleF(0f, 0f, tx - 10f, Height), Theme.Right);
            }

            if (_checked)
            {
                Theme.FillHRound(g, track, th / 2f, Theme.Accent, Theme.Accent2);
                Theme.DrawRound(g, track, th / 2f, Theme.A(Color.White, 30), 1f);
            }
            else
            {
                Theme.FillRound(g, track, th / 2f, Color.FromArgb(27, 32, 43));
                Theme.DrawRound(g, track, th / 2f, _hover ? Theme.A(Theme.BorderLight, 200) : Theme.Border, 1f);
            }

            float knobR = 6.5f;
            float knobCx = _checked ? track.Right - th / 2f : track.X + th / 2f;
            float knobCy = track.Y + th / 2f;
            if (_checked) Theme.Glow(g, new RectangleF(knobCx - 12f, knobCy - 12f, 24f, 24f), Color.White, 0.25);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 249, 255)))
                g.FillEllipse(b, knobCx - knobR, knobCy - knobR, knobR * 2f, knobR * 2f);
            using (Pen pen = new Pen(Theme.A(Color.Black, 40), 1f))
                g.DrawEllipse(pen, knobCx - knobR, knobCy - knobR, knobR * 2f, knobR * 2f);
        }
    }

    // ------------------------------------------------------------------ slider
    public sealed class DarkSlider : Control
    {
        private double _min;
        private double _max = 1.0;
        private double _value;
        private double _step = 0.05;
        private string _caption = "";
        private bool _percent;
        private string _suffix = "x";
        private int _digits = 1;
        private bool _hover;
        private bool _drag;
        private RectangleF _track;

        public event Action<double> ValueChanged;

        public DarkSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.StandardClick, false);
            BackColor = Theme.Card;
            TabStop = false;
            Cursor = Cursors.Hand;
        }

        public double Min { get { return _min; } set { _min = value; Invalidate(); } }
        public double Max { get { return _max; } set { _max = value; Invalidate(); } }
        public double Step { get { return _step; } set { _step = value; } }
        public bool Percent { get { return _percent; } set { _percent = value; Invalidate(); } }
        public string Suffix { get { return _suffix; } set { _suffix = value; Invalidate(); } }
        public int Digits { get { return _digits; } set { _digits = value; Invalidate(); } }
        public bool Dragging { get { return _drag; } }

        public string Caption
        {
            get { return _caption; }
            set { _caption = value != null ? value : ""; Invalidate(); }
        }

        public double Value
        {
            get { return _value; }
        }

        public void SetValueQuietly(double value)
        {
            double v = Clamp(value);
            if (Math.Abs(v - _value) < 0.0001) return;
            _value = v;
            Invalidate();
        }

        private double Clamp(double v)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            return v;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double baseValue = _value;
            double next = baseValue + (_step * (e.Delta > 0 ? 1 : -1));
            SetValue(next);
        }

        private void SetFromX(int x)
        {
            if (_track.Width <= 1f) return;
            double ratio = (x - _track.X) / _track.Width;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            SetValue(_min + ratio * (_max - _min));
        }

        private void SetValue(double raw)
        {
            double v = Clamp(raw);
            if (_step > 0)
            {
                double steps = Math.Round((v - _min) / _step);
                v = Clamp(_min + steps * _step);
            }
            bool changed = Math.Abs(v - _value) > 0.0001;
            _value = v;
            Invalidate();
            if (changed)
            {
                Action<double> h = ValueChanged;
                if (h != null) h(_value);
            }
        }

        private string ValueText()
        {
            if (_percent) return ((int)Math.Round(_value * 100.0)).ToString(CultureInfo.InvariantCulture) + "%";
            return _value.ToString("F" + _digits, CultureInfo.InvariantCulture) + _suffix;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_caption.Length > 0)
                Theme.Label(g, _caption, Theme.F8_5, Theme.TextDim,
                           new RectangleF(1f, 1f, Width * 0.6f, 15f), Theme.Left);
            Theme.Label(g, ValueText(), Theme.M8, _drag || _hover ? Theme.Text : Theme.TextDim,
                       new RectangleF(Width * 0.4f, 1f, Width * 0.6f - 1f, 15f), Theme.Right);

            float ty = Height - 15f;
            _track = new RectangleF(2f, ty + 1.5f, Math.Max(4f, Width - 4f), 4.5f);
            Theme.FillRound(g, _track, 2.25f, Color.FromArgb(27, 32, 43));

            double ratio = _max > _min ? (_value - _min) / (_max - _min) : 0;
            float fillW = (float)(_track.Width * ratio);
            if (fillW > 1.5f)
            {
                RectangleF fill = new RectangleF(_track.X, _track.Y, fillW, _track.Height);
                Theme.FillHRound(g, fill, 2.25f, Theme.Accent, Theme.Accent2);
            }

            float cx = _track.X + fillW;
            float cy = _track.Y + _track.Height / 2f;
            if (_hover || _drag)
                Theme.Glow(g, new RectangleF(cx - 13f, cy - 13f, 26f, 26f),
                           Theme.Mix(Theme.Accent, Theme.Accent2, ratio), 0.55);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(247, 250, 255)))
                g.FillEllipse(b, cx - 5.5f, cy - 5.5f, 11f, 11f);
            using (Pen pen = new Pen(Theme.Accent, 1.4f))
                g.DrawEllipse(pen, cx - 5.5f, cy - 5.5f, 11f, 11f);
        }
    }

    // -------------------------------------------------------------- color swatch
    public sealed class ColorSwatch : Control
    {
        private Rgb _color = new Rgb(255, 0, 60);
        private bool _usesColor = true;
        private bool _hover;

        public event EventHandler Picked;

        public ColorSwatch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.StandardClick, false);
            BackColor = Theme.Card;
            TabStop = false;
        }

        public void Set(Rgb color, bool usesColor)
        {
            bool changed = !Rgb.Same(_color, color) || _usesColor != usesColor;
            _color = color;
            _usesColor = usesColor;
            Cursor = usesColor ? Cursors.Hand : Cursors.Default;
            if (changed) Invalidate();
        }

        public Rgb CurrentColor { get { return _color; } }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_usesColor || !ClientRectangle.Contains(e.Location)) return;
            EventHandler h = Picked;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillVRound(g, r, 7f, Color.FromArgb(30, 36, 48), Color.FromArgb(22, 27, 36));
            Theme.DrawRound(g, r, 7f, _usesColor && _hover ? Theme.A(Theme.Accent, 130) : Theme.Border, 1f);

            float cy = Height / 2f;
            float cx = r.X + 14f;
            Color dotColor = _usesColor ? Theme.RgbColor(_color) : Theme.A(Theme.TextFaint, 140);
            Theme.Dot(g, cx, cy, 7.2f, dotColor);
            using (Pen ring = new Pen(Theme.A(Color.White, _usesColor ? 60 : 24), 1f))
                g.DrawEllipse(ring, cx - 7.2f, cy - 7.2f, 14.4f, 14.4f);

            string hex = _color.ToHex();
            Theme.Label(g, hex, Theme.M8, _usesColor ? Theme.TextDim : Theme.TextFaint,
                       new RectangleF(cx + 13f, 0f, Width - cx - 30f, Height), Theme.Left);

            if (_usesColor)
            {
                float chx = r.Right - 13f;
                using (Pen pen = new Pen(Theme.A(Theme.TextDim, _hover ? 230 : 150), 1.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, chx - 4f, cy - 2f, chx, cy + 2.2f);
                    g.DrawLine(pen, chx, cy + 2.2f, chx + 4f, cy - 2f);
                }
            }
        }
    }

    // ---------------------------------------------------------------- log panel
    public sealed class LogPanel : Control
    {
        private string[] _lines = new string[0];
        private string _lastKey = null;

        public LogPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Bg;
            TabStop = false;
        }

        public void SetLines(string[] lines)
        {
            string[] next = lines != null ? lines : new string[0];
            string key = string.Join("\u0001", next);
            if (key == _lastKey) return;      // log text changes only occasionally
            _lastKey = key;
            _lines = next;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.DrawCard(g, r, Theme.CardRadius);

            Theme.Label(g, "最近活动", Theme.F9B, Theme.TextDim,
                       new RectangleF(16f, 9f, 120f, 18f), Theme.Left);
            int count = _lines.Length;
            Theme.Label(g, count > 0 ? count + " 条" : "", Theme.F8, Theme.TextFaint,
                       new RectangleF(Width - 90f, 9f, 74f, 18f), Theme.Right);

            Theme.HLine(g, 16f, Width - 16f, 27f, Theme.A(Theme.Border, 120));

            int lineH = 15;
            int maxLines = Math.Max(0, (Height - 34) / lineH);
            if (maxLines <= 0) return;
            int n = Math.Min(maxLines, count);
            if (n == 0)
            {
                Theme.Label(g, "暂无活动", Theme.F8, Theme.TextFaint,
                           new RectangleF(16f, 32f, Width - 32f, 16f), Theme.Left);
                return;
            }

            for (int k = 0; k < n; k++)
            {
                string line = _lines[count - n + k];
                bool newest = k == n - 1;
                Color c = newest ? Theme.TextDim : Theme.A(Theme.TextDim, 118);
                float y = 32f + k * lineH;
                if (newest)
                    Theme.Dot(g, 12.5f, y + 7.5f, 2.2f, Theme.A(Theme.Accent, 190));
                Theme.Label(g, line, Theme.F8, c,
                           new RectangleF(20f, y, Width - 36f, lineH + 1f), Theme.EllipsisLeft);
            }
        }
    }

    // --------------------------------------------------------------- dark menu
    public sealed class DarkMenu : ContextMenuStrip
    {
        public DarkMenu()
        {
            BackColor = Color.FromArgb(23, 28, 38);
            ForeColor = Theme.Text;
            Font = Theme.F9;
            ShowImageMargin = false;
            ShowCheckMargin = true;
            Padding = new Padding(4, 6, 4, 6);
            Renderer = new DarkMenuRenderer();
            DropShadowEnabled = true;
            AutoSize = true;
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
            Theme.FillRound(g, r, 8f, Color.FromArgb(23, 28, 38));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, e.ToolStrip.Width - 1f, e.ToolStrip.Height - 1f);
            Theme.DrawRound(g, r, 8f, Theme.A(Theme.BorderLight, 210), 1f);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(2f, 1f, e.Item.Width - 4f, e.Item.Height - 2f);
            Theme.FillRound(g, r, 6f, Theme.A(Theme.Accent, 30));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color;
            if (!e.Item.Enabled) color = Theme.TextFaint;
            else if (e.Item.Selected) color = Theme.Text;
            else color = e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Checked ? Theme.Text : Theme.TextDim;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                  TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle ir = e.ImageRectangle;
            float cx = ir.X + ir.Width / 2f;
            float cy = ir.Y + ir.Height / 2f;
            Theme.Dot(g, cx, cy, 3.0f, Theme.Accent);
            Theme.Dot(g, cx, cy, 6.0f, Theme.A(Theme.Accent, 40));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Graphics g = e.Graphics;
            float y = e.Item.Height / 2f;
            Theme.HLine(g, 8f, e.Item.Width - 8f, y, Theme.A(Theme.Border, 190));
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.A(Theme.Accent, 30); } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.A(Theme.Accent, 30); } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.A(Theme.Accent, 30); } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(23, 28, 38); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(23, 28, 38); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(23, 28, 38); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(23, 28, 38); } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.A(Theme.Accent, 24); } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.A(Theme.Accent, 24); } }
    }
}
