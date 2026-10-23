// PrismSync native app - one device card: name, detail, on/off switch, status
// dot, error text and (for the headset) the firmware-mode dropdown.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public sealed class DeviceCard : Control
    {
        private static readonly string[] ModeNames = new string[]
        {
            "常亮", "呼吸", "繁星", "循环", "随波"
        };

        private readonly string _id;
        private readonly string _title;
        private readonly Color _accent;
        private DeviceRuntimeState _state;
        private int _mode;
        private bool _hover;
        private bool _toggleHover;
        private bool _modeHover;
        private string _lastStateKey;
        private Rectangle _toggleRect;
        private Rectangle _modeRect;
        private DarkMenu _menu;
        private ToolStripMenuItem[] _menuItems;
        private ToolTip _tip;

        public event Action<string, bool> ToggleRequested;
        public event Action<int> ModeRequested;

        public string DeviceId { get { return _id; } }

        public DeviceCard(string id, string title, Color accent)
        {
            _id = id;
            _title = title;
            _accent = accent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardClick, false);
            BackColor = Theme.Card;
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public void Update(DeviceRuntimeState state, int headsetMode)
        {
            string error = state != null ? state.Error : "";
            string status = state != null ? state.Status : "";
            string detail = state != null ? state.Detail : "";
            bool enabled = state != null && state.Enabled;
            bool alive = state != null && state.Alive;
            string key = (enabled ? "1" : "0") + (alive ? "1" : "0") + "|" +
                         headsetMode + "|" + error + "|" + status + "|" + detail;
            if (key == _lastStateKey) return;   // nothing visible changed
            _lastStateKey = key;
            _state = state;
            _mode = headsetMode;
            if (_id == "headset")
            {
                if (_tip == null) _tip = new ToolTip();
                _tip.SetToolTip(this, string.IsNullOrEmpty(error) ? "" : error);
            }
            Invalidate();
        }

        private bool EnabledDevice
        {
            get { return _state != null && _state.Enabled; }
        }

        private bool Alive
        {
            get { return _state != null && _state.Alive; }
        }

        private string StatusText()
        {
            if (_state == null) return "未知";
            if (!_state.Enabled) return "已关闭";
            return _state.Alive ? "在线" : "未连接";
        }

        private Color StatusColor()
        {
            if (_state == null) return Theme.TextFaint;
            if (!_state.Enabled) return Theme.TextFaint;
            return _state.Alive ? Theme.Good : Theme.Warn;
        }

        private string ErrorText()
        {
            if (_state == null || !_state.Enabled) return "";
            return _state.Error != null ? _state.Error : "";
        }

        // ------------------------------------------------------------------ mouse
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
            _toggleHover = false;
            _modeHover = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool th = _toggleRect.Contains(e.Location);
            bool mh = _id == "headset" && _modeRect.Contains(e.Location);
            if (th != _toggleHover || mh != _modeHover)
            {
                _toggleHover = th;
                _modeHover = mh;
                Cursor = (th || mh) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (_toggleRect.Contains(e.Location))
            {
                bool next = _state == null || !_state.Enabled;
                Action<string, bool> h = ToggleRequested;
                if (h != null) h(_id, next);
                return;
            }
            if (_id == "headset" && _modeRect.Contains(e.Location)) ShowModeMenu();
        }

        private void ShowModeMenu()
        {
            if (_menu == null) BuildMenu();
            for (int i = 0; i < _menuItems.Length; i++) _menuItems[i].Checked = i == _mode;
            Point p = new Point(_modeRect.X, _modeRect.Bottom + 3);
            _menu.Show(this, p);
        }

        private void BuildMenu()
        {
            _menu = new DarkMenu();
            _menuItems = new ToolStripMenuItem[ModeNames.Length];
            for (int i = 0; i < ModeNames.Length; i++)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(ModeNames[i]);
                item.Tag = i;
                item.Click += delegate (object s, EventArgs e)
                {
                    ToolStripMenuItem mi = s as ToolStripMenuItem;
                    if (mi == null) return;
                    int index = (int)mi.Tag;
                    _mode = index;
                    Action<int> h = ModeRequested;
                    if (h != null) h(index);
                    Invalidate();
                };
                _menuItems[i] = item;
                _menu.Items.Add(item);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_menu != null) { _menu.Dispose(); _menu = null; }
                if (_tip != null) { _tip.Dispose(); _tip = null; }
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ paint
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            bool enabled = EnabledDevice;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.DrawCard(g, r, Theme.CardRadius);
            if (_hover)
            {
                Theme.FillRound(g, r, Theme.CardRadius, Theme.A(Color.White, 4));
                Theme.DrawRound(g, r, Theme.CardRadius, Theme.A(Theme.BorderLight, 170), 1f);
            }

            // status coloured hairline at the top of the card
            Color sc = StatusColor();
            if (enabled || Alive)
                Theme.HLine(g, r.X + 14f, r.Right - 14f, r.Y + 1.2f, Theme.A(sc, 90));

            // icon
            Color iconColor = enabled ? _accent : Theme.A(Theme.TextFaint, 190);
            DrawDeviceIcon(g, new RectangleF(14f, 12f, 22f, 22f), iconColor);

            // title + toggle
            Theme.Label(g, _title, Theme.F9B, enabled ? Theme.Text : Theme.TextDim,
                       new RectangleF(44f, 10f, Width - 44f - 54f, 17f), Theme.Left);
            _toggleRect = new Rectangle(Width - 14 - 34, 13, 34, 18);
            PaintToggle(g, _toggleRect, enabled, _toggleHover);

            // detail
            string detail = _state != null && _state.Detail != null ? _state.Detail : "";
            Theme.Label(g, detail, Theme.F8, Theme.TextFaint,
                       new RectangleF(44f, 31f, Width - 58f, 14f), Theme.EllipsisLeft);

            if (_id == "headset")
            {
                _modeRect = new Rectangle(14, Height - 40, Math.Min(104, Width - 100), 21);
                PaintModeChip(g, _modeRect);
                PaintStatus(g, Width - 14f, Height - 29, true);
            }
            else
            {
                PaintStatus(g, 14f, Height - 29, false);
                string err = ErrorText();
                if (err.Length > 0)
                {
                    float errX = 96f;
                    Theme.Label(g, err, Theme.F8, Theme.A(Theme.Warn, 190),
                               new RectangleF(errX, Height - 37f, Width - errX - 14f, 15f), Theme.EllipsisRight);
                }
            }
        }

        private void PaintStatus(Graphics g, float x, float cy, bool rightAligned)
        {
            Color sc = StatusColor();
            string text = StatusText();
            Size ts = Theme.Measure(text, Theme.F8_5);
            float dotR = 3f;
            if (rightAligned)
            {
                float textX = x - ts.Width;
                Theme.Dot(g, textX - dotR - 5f, cy, dotR, sc);
                if (Alive) Theme.Glow(g, new RectangleF(textX - dotR - 11f, cy - 6f, 12f, 12f), sc, 0.5);
                Theme.Label(g, text, Theme.F8_5, sc, new RectangleF(textX, cy - 8f, ts.Width + 2f, 16f), Theme.Left);
            }
            else
            {
                Theme.Dot(g, x + dotR, cy, dotR, sc);
                if (Alive) Theme.Glow(g, new RectangleF(x - 3f, cy - 6f, 12f, 12f), sc, 0.5);
                Theme.Label(g, text, Theme.F8_5, sc, new RectangleF(x + dotR + 7f, cy - 8f, ts.Width + 4f, 16f), Theme.Left);
            }
        }

        private void PaintToggle(Graphics g, Rectangle track, bool on, bool hover)
        {
            RectangleF t = new RectangleF(track.X, track.Y, track.Width, track.Height);
            if (on)
            {
                Theme.FillHRound(g, t, t.Height / 2f, Theme.Accent, Theme.Accent2);
                Theme.DrawRound(g, t, t.Height / 2f, Theme.A(Color.White, 28), 1f);
            }
            else
            {
                Theme.FillRound(g, t, t.Height / 2f, Color.FromArgb(27, 32, 43));
                Theme.DrawRound(g, t, t.Height / 2f, hover ? Theme.A(Theme.BorderLight, 210) : Theme.Border, 1f);
            }
            float knobR = 6.5f;
            float cx = on ? t.Right - t.Height / 2f : t.X + t.Height / 2f;
            float cy = t.Y + t.Height / 2f;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(246, 249, 255)))
                g.FillEllipse(b, cx - knobR, cy - knobR, knobR * 2f, knobR * 2f);
            using (Pen pen = new Pen(Theme.A(Color.Black, 38), 1f))
                g.DrawEllipse(pen, cx - knobR, cy - knobR, knobR * 2f, knobR * 2f);
        }

        private void PaintModeChip(Graphics g, Rectangle chip)
        {
            RectangleF c = new RectangleF(chip.X, chip.Y, chip.Width, chip.Height);
            Theme.FillRound(g, c, 6f, _modeHover ? Theme.A(Color.White, 13) : Theme.A(Color.White, 6));
            Theme.DrawRound(g, c, 6f, _modeHover ? Theme.A(Theme.BorderLight, 200) : Theme.A(Theme.Border, 220), 1f);
            string name = (_mode >= 0 && _mode < ModeNames.Length) ? ModeNames[_mode] : ModeNames[0];
            Theme.Label(g, "固件 " + name, Theme.F8, _modeHover ? Theme.Text : Theme.TextDim,
                       new RectangleF(c.X + 9f, c.Y, c.Width - 26f, c.Height), Theme.Left);
            float chx = c.Right - 11f;
            float chy = c.Y + c.Height / 2f;
            using (Pen pen = new Pen(Theme.A(Theme.TextDim, _modeHover ? 230 : 150), 1.3f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, chx - 3.2f, chy - 1.4f, chx, chy + 1.8f);
                g.DrawLine(pen, chx, chy + 1.8f, chx + 3.2f, chy - 1.4f);
            }
        }

        private void DrawDeviceIcon(Graphics g, RectangleF r, Color color)
        {
            using (Pen pen = new Pen(color, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (_id == "headset")
                {
                    RectangleF band = new RectangleF(r.X + 3f, r.Y + 2f, r.Width - 6f, r.Height - 7f);
                    g.DrawArc(pen, band, 180f, 180f);
                    Theme.FillRound(g, new RectangleF(r.X + 1.5f, r.Y + 11f, 4.6f, 8.5f), 2f, color);
                    Theme.FillRound(g, new RectangleF(r.Right - 6.1f, r.Y + 11f, 4.6f, 8.5f), 2f, color);
                }
                else if (_id == "laptop")
                {
                    RectangleF screen = new RectangleF(r.X + 3.5f, r.Y + 3f, r.Width - 7f, r.Height - 10f);
                    Theme.DrawRound(g, screen, 2f, color, 1.4f);
                    using (Pen basePen = new Pen(color, 1.6f))
                        g.DrawLine(basePen, r.X + 1f, r.Bottom - 3.5f, r.Right - 1f, r.Bottom - 3.5f);
                    using (Pen stem = new Pen(Theme.A(color, 150), 1.3f))
                        g.DrawLine(stem, r.X + 4f, r.Bottom - 6f, r.Right - 4f, r.Bottom - 6f);
                }
                else
                {
                    RectangleF board = new RectangleF(r.X + 1.5f, r.Y + 5f, r.Width - 3f, r.Height - 10f);
                    Theme.DrawRound(g, board, 2.4f, color, 1.4f);
                    for (int row = 0; row < 3; row++)
                    {
                        float y = board.Y + 3.2f + row * 3.6f;
                        g.DrawLine(pen, board.X + 3f, y, board.Right - 3f, y);
                    }
                }
            }
        }
    }
}
