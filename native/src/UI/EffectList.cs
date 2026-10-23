// PrismSync native app - sidebar effect list: custom-painted, scrollable, with
// a small animated colour thumbnail per effect.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public sealed class EffectList : Control
    {
        private readonly EffectSpec[] _specs;
        private string _selected;
        private double _time;
        private int _updateTicks;
        private int _hover = -1;
        private int _scroll;
        private int _downIndex = -1;
        private readonly int _itemH = 36;

        private bool _hasScroll;
        private bool _thumbDrag;
        private int _thumbDragY;
        private int _thumbDragStartScroll;
        private Rectangle _trackRect;
        private Rectangle _thumbRect;

        public event Action<string> EffectChosen;

        public EffectList(EffectSpec[] specs)
        {
            _specs = specs != null ? specs : new EffectSpec[0];
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            BackColor = Theme.Sidebar;
            ForeColor = Theme.Text;
            TabStop = true;
            Cursor = Cursors.Default;
            if (_specs.Length > 0) Update(null, 0.0);
        }

        // ---------------------------------------------------------------- update
        public void Update(AppSnapshot snap, double time)
        {
            _time = time;
            string id = snap != null ? snap.EffectId : null;
            bool changed = false;
            if (id != null && id != _selected)
            {
                _selected = id;
                EnsureVisible();
                changed = true;
            }
            // The coloured dots animate, but 10 fps is plenty and repainting 18 rows
            // every UI tick costs several percent CPU.
            if (changed || (++_updateTicks % 2) == 0) Invalidate();
        }

        public string SelectedId { get { return _selected; } }

        private int IndexOf(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < _specs.Length; i++) if (_specs[i].Id == id) return i;
            return -1;
        }

        private void EnsureVisible()
        {
            int index = IndexOf(_selected);
            if (index < 0) return;
            int contentH = _specs.Length * _itemH;
            int maxScroll = Math.Max(0, contentH - Height);
            int top = index * _itemH;
            if (top < _scroll) _scroll = top;
            else if (top + _itemH > _scroll + Height) _scroll = top + _itemH - Height;
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;
        }

        private int IndexAt(int y)
        {
            int i = (y + _scroll) / _itemH;
            if (i < 0 || i >= _specs.Length) return -1;
            if (y < 0 || y > Height) return -1;
            return i;
        }

        // ----------------------------------------------------------------- paint
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush bg = new SolidBrush(Theme.Sidebar)) g.FillRectangle(bg, ClientRectangle);
            if (_specs.Length == 0) return;

            int contentH = _specs.Length * _itemH;
            _hasScroll = contentH > Height;
            int maxScroll = Math.Max(0, contentH - Height);
            if (_scroll > maxScroll) _scroll = maxScroll;
            if (_scroll < 0) _scroll = 0;

            int listW = Width - (_hasScroll ? 9 : 0);
            for (int i = 0; i < _specs.Length; i++)
            {
                int y = i * _itemH - _scroll;
                if (y + _itemH < 0 || y > Height) continue;
                RectangleF item = new RectangleF(3f, y + 1f, listW - 6f, _itemH - 2f);
                PaintItem(g, item, i);
            }

            if (_hasScroll) PaintScrollbar(g, contentH, maxScroll);
        }

        private void PaintItem(Graphics g, RectangleF item, int index)
        {
            EffectSpec spec = _specs[index];
            bool selected = spec.Id == _selected;
            bool hover = index == _hover && !selected;

            if (selected)
            {
                Theme.FillRound(g, item, 8f, Theme.A(Theme.Accent, 20));
                Theme.DrawRound(g, item, 8f, Theme.A(Theme.Accent, 52), 1f);
                float barX = item.X + 1.5f;
                float barY = item.Y + 9f;
                float barH = item.Height - 18f;
                if (barH > 2f)
                {
                    using (GraphicsPath bp = Theme.Rounded(new RectangleF(barX, barY, 2.5f, barH), 1.25f))
                    using (LinearGradientBrush b = new LinearGradientBrush(
                               new RectangleF(barX, barY, 2.5f, Math.Max(2f, barH)),
                               Theme.Accent, Theme.Accent2, LinearGradientMode.Vertical))
                    {
                        b.WrapMode = WrapMode.TileFlipXY;
                        g.FillPath(b, bp);
                    }
                }
            }
            else if (hover)
            {
                Theme.FillRound(g, item, 8f, Theme.A(Color.White, 7));
            }

            float dotX = item.X + 12f;
            float dotY = item.Y + (item.Height - 22f) / 2f;
            PaintDot(g, new RectangleF(dotX, dotY, 22f, 22f), index, spec, selected);

            Font font = selected ? Theme.F9B : Theme.F9;
            Color textColor = selected ? Theme.Text : (hover ? Theme.TextDim : Theme.TextDim);
            RectangleF textRect = new RectangleF(item.X + 42f, item.Y, item.Width - 54f, item.Height);
            Theme.Label(g, spec.Name, font, textColor, textRect, Theme.EllipsisLeft);

            if (selected)
            {
                Theme.Dot(g, item.Right - 14f, item.Y + item.Height / 2f, 2.6f, Theme.A(Theme.Accent, 220));
            }
        }

        private void PaintDot(Graphics g, RectangleF r, int index, EffectSpec spec, bool selected)
        {
            double h = BaseHue(spec.Id);
            bool animated = spec.Id == "rainbow" || spec.Id == "flow" || spec.Id == "hue" ||
                            spec.Id == "plasma" || spec.Id == "stars" || spec.Id == "trio";
            if (animated) h += _time * 0.055;
            double h2 = h + (spec.Id == "rainbow" ? 0.46 : 0.19);
            Color c1 = Theme.RgbColor(Rgb.FromHsv(h, 0.72, 0.98));
            Color c2 = Theme.RgbColor(Rgb.FromHsv(h2, 0.86, 0.84));

            if (selected)
                Theme.Glow(g, RectangleF.Inflate(r, 6f, 6f), c1, 0.55);

            float angle = 40f + (float)((_time * 15.0 + index * 27.0) % 360.0);
            using (GraphicsPath p = Theme.Rounded(r, 6.5f))
            using (LinearGradientBrush b = new LinearGradientBrush(
                       new RectangleF(r.X, r.Y, Math.Max(2f, r.Width), Math.Max(2f, r.Height)),
                       c1, c2, angle))
            {
                b.WrapMode = WrapMode.TileFlipXY;
                g.FillPath(b, p);
            }
            using (SolidBrush gloss = new SolidBrush(Theme.A(Color.White, 72)))
                g.FillEllipse(gloss, r.X + r.Width * 0.20f, r.Y + r.Height * 0.14f,
                              r.Width * 0.32f, r.Height * 0.27f);
        }

        private void PaintScrollbar(Graphics g, int contentH, int maxScroll)
        {
            _trackRect = new Rectangle(Width - 6, 4, 3, Math.Max(6, Height - 8));
            Theme.FillRound(g, _trackRect, 1.5f, Theme.A(Color.White, 9));

            int thumbH = Math.Max(26, (int)(Height * (Height / (float)contentH)));
            if (thumbH > _trackRect.Height) thumbH = _trackRect.Height;
            float pos = maxScroll > 0 ? (float)_scroll / maxScroll : 0f;
            int thumbY = _trackRect.Y + (int)(pos * (_trackRect.Height - thumbH));
            _thumbRect = new Rectangle(_trackRect.X, thumbY, 3, thumbH);
            Color thumbColor = (_thumbDrag || _hover == -2) ? Theme.A(Theme.Accent, 150) : Theme.A(Color.White, 44);
            Theme.FillRound(g, _thumbRect, 1.5f, thumbColor);
        }

        // ------------------------------------------------------------------ mouse
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_thumbDrag)
            {
                if (_trackRect.Height > 0 && _thumbRect.Height > 0)
                {
                    int usable = _trackRect.Height - _thumbRect.Height;
                    int contentH = _specs.Length * _itemH;
                    int maxScroll = Math.Max(0, contentH - Height);
                    if (usable > 0 && maxScroll > 0)
                    {
                        _scroll = _thumbDragStartScroll +
                                  (int)((e.Y - _thumbDragY) * (maxScroll / (float)usable));
                        Invalidate();
                    }
                }
                return;
            }

            int index = IndexAt(e.Y);
            bool overThumb = _hasScroll && _thumbRect.Contains(e.X, e.Y);
            if (overThumb)
            {
                if (_hover != -2) { _hover = -2; Invalidate(); }
                Cursor = Cursors.Default;
                return;
            }
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_hasScroll && _thumbRect.Contains(e.X, e.Y))
            {
                _thumbDrag = true;
                _thumbDragY = e.Y;
                _thumbDragStartScroll = _scroll;
                return;
            }
            _downIndex = IndexAt(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_thumbDrag)
            {
                _thumbDrag = false;
                Invalidate();
                return;
            }
            int index = IndexAt(e.Y);
            if (index >= 0 && index == _downIndex)
            {
                _selected = _specs[index].Id;
                Action<string> h = EffectChosen;
                if (h != null) h(_selected);
            }
            _downIndex = -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_hasScroll)
            {
                return;
            }
            int contentH = _specs.Length * _itemH;
            int maxScroll = Math.Max(0, contentH - Height);
            _scroll -= (e.Delta / 120) * _itemH * 2;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > maxScroll) _scroll = maxScroll;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (CanFocus) Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int index = IndexOf(_selected);
            if (e.KeyCode == Keys.Down)
            {
                index = Math.Min(_specs.Length - 1, index + 1);
                Choose(index);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                index = Math.Max(0, index < 0 ? 0 : index - 1);
                Choose(index);
                e.Handled = true;
            }
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _specs.Length) return;
            _selected = _specs[index].Id;
            EnsureVisible();
            Invalidate();
            Action<string> h = EffectChosen;
            if (h != null) h(_selected);
        }

        // ------------------------------------------------------------- palette
        private static double BaseHue(string id)
        {
            switch (id)
            {
                case "trio": return 0.52;
                case "aurora": return 0.44;
                case "duo": return 0.97;
                case "flow": return 0.02;
                case "wave": return 0.55;
                case "comet": return 0.60;
                case "rainbow": return 0.10;
                case "hue": return 0.78;
                case "breathe": return 0.95;
                case "static": return 0.96;
                case "plasma": return 0.76;
                case "stars": return 0.58;
                case "meteor": return 0.86;
                case "fire": return 0.04;
                case "ripple": return 0.48;
                case "keyhit": return 0.50;
                case "screen": return 0.62;
                case "audio": return 0.66;
                default: return 0.55;
            }
        }
    }
}
