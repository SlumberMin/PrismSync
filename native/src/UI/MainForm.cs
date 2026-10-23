// PrismSync native app - main window.
//
// Pure WinForms custom painting (no designer, no third party libraries): dark
// shell, sidebar effect list, live desktop preview, lighting controls, device
// cards and an activity log. Public surface used by Program / TrayController:
// constructor, ShowFromTray(), ForceClose().
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace PrismSync.UI
{
    public sealed class MainForm : Form
    {
        private readonly AppController _app;
        private readonly KeyLayout _layout;
        private readonly Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private AppSnapshot _snap;
        private bool _forceClose;
        private int _sideWidth = 250;
        private int _listTop = 112;
        private bool _adminShown;
        private bool _logShown = true;
        private Bitmap _staticCache;
        private string _cacheKey;

        // widgets
        private EffectList _effects;
        private DesktopPreview _preview;
        private DarkSlider _speed;
        private DarkSlider _brightness;
        private ToggleSwitch _uniform;
        private ColorSwatch _color;
        private DeviceCard _cardHeadset;
        private DeviceCard _cardLaptop;
        private DeviceCard _cardKeyboard;
        private FlatButton _primary;
        private FlatButton _rescan;
        private FlatButton _elevate;
        private LogPanel _log;

        // painted chrome
        private Rectangle _topRect;
        private Rectangle _previewRect;
        private Rectangle _controlsRect;
        private Rectangle _devicesRect;
        private Rectangle _adminRect;
        private Rectangle _actionRect;
        private Rectangle _logRect;

        public MainForm(AppController app)
        {
            _app = app;
            _layout = app.Engine.Layout;

            Text = "棱镜同步 PrismSync";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 660);
            MinimumSize = new Size(920, 600);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F9;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            if (app.Settings != null && app.Settings.WindowX >= 0 && app.Settings.WindowY >= 0)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(app.Settings.WindowX, app.Settings.WindowY);
            }

            _snap = SafeSnapshot();
            if (_snap == null) _snap = new AppSnapshot();

            BuildWidgets();
            DoLayout();
            ApplySnapshot();

            _timer = new Timer();
            _timer.Interval = 50;   // 20 fps UI is enough; the lights itself run at 30 fps
            _timer.Tick += OnTick;
            _timer.Start();
        }

        // ------------------------------------------------------------------ setup
        private void BuildWidgets()
        {
            _effects = new EffectList(_app.Effects);
            _effects.EffectChosen += delegate (string id) { _app.SetEffect(id); };
            Controls.Add(_effects);

            _preview = new DesktopPreview();
            Controls.Add(_preview);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--no-preview") >= 0)
                _preview.Visible = false;

            _speed = new DarkSlider();
            _speed.Min = 0.3;
            _speed.Max = 2.5;
            _speed.Step = 0.05;
            _speed.Digits = 1;
            _speed.Suffix = "x";
            _speed.Caption = "速度";
            _speed.ValueChanged += delegate (double v) { _app.SetSpeed(v); };
            Controls.Add(_speed);

            _brightness = new DarkSlider();
            _brightness.Min = 0.05;
            _brightness.Max = 1.0;
            _brightness.Step = 0.01;
            _brightness.Percent = true;
            _brightness.Caption = "亮度";
            _brightness.ValueChanged += delegate (double v) { _app.SetBrightness(v); };
            Controls.Add(_brightness);

            _color = new ColorSwatch();
            _color.Picked += delegate { PickColor(); };
            Controls.Add(_color);

            _uniform = new ToggleSwitch();
            _uniform.Caption = "所有设备同色";
            _uniform.Toggled += delegate (bool on) { _app.SetUniform(on); };
            Controls.Add(_uniform);

            _cardHeadset = new DeviceCard("headset", "耳机", Theme.Accent);
            WireCard(_cardHeadset);
            _cardHeadset.ModeRequested += delegate (int mode) { _app.SetHeadsetMode(mode); };
            Controls.Add(_cardHeadset);

            _cardLaptop = new DeviceCard("laptop", "笔记本", Theme.Accent2);
            WireCard(_cardLaptop);
            Controls.Add(_cardLaptop);

            _cardKeyboard = new DeviceCard("keyboard", "键盘", Theme.Good);
            WireCard(_cardKeyboard);
            Controls.Add(_cardKeyboard);

            _primary = new FlatButton();
            _primary.Kind = ButtonKind.Primary;
            _primary.Caption = "启动灯效";
            _primary.Icon = ButtonIcon.Play;
            _primary.Click += delegate { _app.Toggle(); };
            Controls.Add(_primary);

            _rescan = new FlatButton();
            _rescan.Kind = ButtonKind.Secondary;
            _rescan.Caption = "重新扫描";
            _rescan.Icon = ButtonIcon.Refresh;
            _rescan.Click += delegate { _app.RescanDevices(); };
            Controls.Add(_rescan);

            _elevate = new FlatButton();
            _elevate.Kind = ButtonKind.Secondary;
            _elevate.Small = true;
            _elevate.Caption = "以管理员身份重启";
            _elevate.Icon = ButtonIcon.Shield;
            _elevate.Click += delegate { RestartElevated(); };
            _elevate.Visible = false;
            Controls.Add(_elevate);

            _log = new LogPanel();
            Controls.Add(_log);
        }

        private void WireCard(DeviceCard card)
        {
            card.ToggleRequested += delegate (string id, bool enabled) { _app.SetDeviceEnabled(id, enabled); };
        }

        private AppSnapshot SafeSnapshot()
        {
            try { return _app.Snapshot(); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ layout
        private void DoLayout()
        {
            if (_effects == null || _preview == null || _primary == null || _log == null) return;
            int W = ClientSize.Width;
            int H = ClientSize.Height;
            if (W < 200 || H < 200) return;

            _sideWidth = W < 940 ? 230 : 250;
            const int pad = 16;
            const int gap = 10;
            int contentX = _sideWidth + pad;
            int contentW = W - contentX - pad;
            if (contentW < 200) return;

            const int topH = 34;
            bool compact = H < 640;
            int controlsH = compact ? 92 : 100;
            int devicesH = 100;
            int actionH = 40;
            int logH = compact ? 60 : 92;
            int adminH = _adminShown ? 32 : 0;

            int fixedH = pad * 2 + topH + controlsH + devicesH + actionH + logH + gap * 5;
            if (adminH > 0) fixedH += adminH + gap;
            int previewH = H - fixedH;

            _logShown = true;
            if (previewH < 132)
            {
                _logShown = false;
                int noLog = pad * 2 + topH + controlsH + devicesH + actionH + gap * 4;
                if (adminH > 0) noLog += adminH + gap;
                previewH = H - noLog;
            }
            if (previewH < 90) previewH = 90;

            int y = pad;
            _topRect = new Rectangle(contentX, y, contentW, topH);
            y += topH + gap;
            _previewRect = new Rectangle(contentX, y, contentW, previewH);
            y += previewH + gap;
            _controlsRect = new Rectangle(contentX, y, contentW, controlsH);
            y += controlsH + gap;
            _devicesRect = new Rectangle(contentX, y, contentW, devicesH);
            y += devicesH + gap;

            if (_adminShown)
            {
                _adminRect = new Rectangle(contentX, y, contentW, adminH);
                y += adminH + gap;
            }
            else
            {
                _adminRect = Rectangle.Empty;
            }

            _actionRect = new Rectangle(contentX, y, contentW, actionH);
            y += actionH + gap;
            _logRect = new Rectangle(contentX, y, contentW, logH);

            // ---- sidebar ----
            _listTop = 112;
            _effects.SetBounds(10, _listTop, _sideWidth - 20, Math.Max(40, H - _listTop - 72));

            // ---- preview ----
            _preview.SetBounds(_previewRect.X, _previewRect.Y, _previewRect.Width, _previewRect.Height);

            // ---- controls card ----
            const int colorW = 104;
            const int uniformW = 176;
            _color.SetBounds(_controlsRect.Right - pad - colorW, _controlsRect.Y + 8, colorW, 26);
            _uniform.SetBounds(_color.Left - 12 - uniformW, _controlsRect.Y + 8, uniformW, 26);
            int sliderW = (_controlsRect.Width - pad * 2 - 12) / 2;
            int sliderY = _controlsRect.Y + 44;
            _speed.SetBounds(_controlsRect.X + pad, sliderY, sliderW, 44);
            _brightness.SetBounds(_controlsRect.X + pad + sliderW + 12, sliderY, sliderW, 44);

            // ---- device row ----
            const int cardGap = 8;
            int cardW = (_devicesRect.Width - cardGap * 2) / 3;
            int cardH = _devicesRect.Height;
            _cardHeadset.SetBounds(_devicesRect.X, _devicesRect.Y, cardW, cardH);
            _cardLaptop.SetBounds(_devicesRect.X + cardW + cardGap, _devicesRect.Y, cardW, cardH);
            _cardKeyboard.SetBounds(_devicesRect.X + (cardW + cardGap) * 2, _devicesRect.Y, cardW, cardH);

            // ---- action row ----
            _primary.SetBounds(_actionRect.X, _actionRect.Y, 148, actionH);
            _rescan.SetBounds(_actionRect.X + 158, _actionRect.Y, 116, actionH);

            // ---- admin banner ----
            _elevate.Visible = _adminShown;
            if (_adminShown)
                _elevate.SetBounds(_adminRect.Right - pad - 148, _adminRect.Y + 4, 148, 24);

            // ---- log ----
            _log.Visible = _logShown;
            if (_logShown) _log.SetBounds(_logRect.X, _logRect.Y, _logRect.Width, _logRect.Height);

            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        // ------------------------------------------------------------------- tick
        private void OnTick(object sender, EventArgs e)
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            AppSnapshot s = SafeSnapshot();
            if (s == null) return;
            _snap = s;
            ApplySnapshot();
            Invalidate();
        }

        private void ApplySnapshot()
        {
            if (_snap == null) return;
            double t = _clock.Elapsed.TotalSeconds;

            _effects.Update(_snap, t);
            _preview.Tick(_snap, _layout);

            if (!_speed.Dragging) _speed.SetValueQuietly(_snap.Speed);
            if (!_brightness.Dragging) _brightness.SetValueQuietly(_snap.Brightness);
            _uniform.SetChecked(_snap.Uniform);
            _color.Set(_snap.Color, _snap.EffectUsesColor);

            UpdateCard(_cardHeadset, "headset");
            UpdateCard(_cardLaptop, "laptop");
            UpdateCard(_cardKeyboard, "keyboard");

            _primary.Caption = _snap.Running ? "暂停灯效" : "启动灯效";
            _primary.Icon = _snap.Running ? ButtonIcon.Pause : ButtonIcon.Play;

            bool adminWanted = !_snap.IsAdmin && DeviceEnabled("laptop");
            if (adminWanted != _adminShown)
            {
                _adminShown = adminWanted;
                DoLayout();
            }

            _log.SetLines(_snap.Log);
        }

        private void UpdateCard(DeviceCard card, string id)
        {
            DeviceRuntimeState state = null;
            if (_snap.Devices != null)
            {
                for (int i = 0; i < _snap.Devices.Length; i++)
                {
                    if (_snap.Devices[i] != null && _snap.Devices[i].Id == id)
                    {
                        state = _snap.Devices[i];
                        break;
                    }
                }
            }
            card.Update(state, _snap.HeadsetMode);
        }

        private bool DeviceEnabled(string id)
        {
            if (_snap == null || _snap.Devices == null) return false;
            for (int i = 0; i < _snap.Devices.Length; i++)
            {
                DeviceRuntimeState d = _snap.Devices[i];
                if (d != null && d.Id == id) return d.Enabled;
            }
            return false;
        }

        // ------------------------------------------------------------------ paint
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // The whole surface is painted in OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Static chrome (background glows, sidebar, cards) is expensive with
            // GDI+ and does not change per frame: paint it once into a cache and
            // only repaint the parts that actually animate. This keeps the UI at
            // 30 fps for a few percent of a core.
            string key = ClientSize.Width + "x" + ClientSize.Height + "|" +
                         _adminShown + "|" + (_snap != null && _snap.IsAdmin);
            if (_staticCache == null || _cacheKey != key ||
                _staticCache.Width != ClientSize.Width || _staticCache.Height != ClientSize.Height)
            {
                if (_staticCache != null) _staticCache.Dispose();
                _staticCache = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                using (Graphics cg = Graphics.FromImage(_staticCache))
                {
                    cg.SmoothingMode = SmoothingMode.AntiAlias;
                    cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    cg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    PaintBackdrop(cg);
                    PaintSidebar(cg);
                    PaintControlsCard(cg);
                    if (_adminShown) PaintAdminBanner(cg);
                }
                _cacheKey = key;
            }
            g.DrawImageUnscaled(_staticCache, 0, 0);
            PaintTopBar(g);
            PaintActionRow(g);
        }

        private void PaintBackdrop(Graphics g)
        {
            Theme.FillV(g, ClientRectangle, Theme.Bg, Theme.Deep);
            Theme.Glow(g, new RectangleF(Width * 0.42f, -ClientSize.Height * 0.30f, Width * 0.95f, ClientSize.Height * 0.80f),
                       Theme.Accent, 0.14);
            Theme.Glow(g, new RectangleF(Width * 0.02f, ClientSize.Height * 0.55f, Width * 0.55f, ClientSize.Height * 0.70f),
                       Theme.Accent2, 0.09);
        }

        private void PaintSidebar(Graphics g)
        {
            Rectangle side = new Rectangle(0, 0, _sideWidth, ClientSize.Height);
            using (SolidBrush b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, side);
            Theme.VLine(g, _sideWidth - 0.5f, 0f, ClientSize.Height, Theme.A(Theme.Border, 130));

            // brand badge
            RectangleF badge = new RectangleF(20f, 20f, 44f, 44f);
            Theme.Glow(g, RectangleF.Inflate(badge, 10f, 10f), Theme.Accent, 0.16);
            Theme.FillVRound(g, badge, 11f, Color.FromArgb(37, 45, 61), Color.FromArgb(22, 27, 37));
            Theme.DrawRound(g, badge, 11f, Theme.A(Theme.BorderLight, 150), 1f);
            try
            {
                Bitmap icon = BrandIcon.Get(64);
                if (icon != null) g.DrawImage(icon, new RectangleF(26f, 26f, 32f, 32f));
            }
            catch { }

            Theme.Label(g, "PrismSync", Theme.F16B, Theme.Text,
                       new RectangleF(76f, 22f, _sideWidth - 90f, 24f), Theme.Left);
            Theme.Label(g, "棱镜同步 · 原生灯效控制 v1.1", Theme.F8_5, Theme.TextDim,
                       new RectangleF(77f, 48f, _sideWidth - 90f, 16f), Theme.Left);

            // section header
            float secY = _listTop - 27f;
            Theme.Label(g, "灯效库", Theme.F9B, Theme.TextDim,
                       new RectangleF(20f, secY, 90f, 18f), Theme.Left);
            Theme.Label(g, _app.Effects.Length.ToString(CultureInfo.InvariantCulture) + " 个",
                       Theme.F8, Theme.TextFaint,
                       new RectangleF(_sideWidth - 74f, secY, 54f, 18f), Theme.Right);

            // tray hint at the bottom
            RectangleF tray = new RectangleF(16f, ClientSize.Height - 58f, _sideWidth - 32f, 42f);
            Theme.FillRound(g, tray, 9f, Theme.A(Color.White, 4));
            Theme.DrawRound(g, tray, 9f, Theme.A(Theme.Border, 150), 1f);
            float gx = tray.X + 14f;
            float gy = tray.Y + 13f;
            Theme.FillRound(g, new RectangleF(gx, gy + 6f, 14f, 10f), 2f, Theme.A(Theme.TextDim, 190));
            using (Pen pen = new Pen(Theme.A(Theme.TextDim, 190), 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, gx + 7f, gy + 1f, gx + 7f, gy + 7f);
                g.DrawLine(pen, gx + 4f, gy + 4f, gx + 7f, gy + 1f);
                g.DrawLine(pen, gx + 10f, gy + 4f, gx + 7f, gy + 1f);
            }
            Theme.Label(g, "关闭窗口后仍在托盘运行", Theme.F8, Theme.TextDim,
                       new RectangleF(tray.X + 38f, tray.Y + 8f, tray.Width - 46f, 14f), Theme.Left);
            Theme.Label(g, "双击托盘图标可重新打开", Theme.F7_5, Theme.TextFaint,
                       new RectangleF(tray.X + 38f, tray.Y + 24f, tray.Width - 46f, 12f), Theme.Left);
        }

        private void PaintTopBar(Graphics g)
        {
            bool running = _snap != null && _snap.Running;
            Color sc = running ? Theme.Good : Theme.Warn;
            string status = running ? "运行中" : "已暂停";
            Size ts = Theme.Measure(status, Theme.F8_5);
            int cy = _topRect.Y + _topRect.Height / 2;
            float pillH = 24f;
            float pillW = ts.Width + 32f;
            RectangleF pill = new RectangleF(_topRect.X, cy - pillH / 2f, pillW, pillH);
            if (running) Theme.Glow(g, RectangleF.Inflate(pill, 5f, 5f), sc, 0.42);
            Theme.FillRound(g, pill, pillH / 2f, Theme.A(sc, 26));
            Theme.DrawRound(g, pill, pillH / 2f, Theme.A(sc, 92), 1f);
            Theme.Dot(g, pill.X + 13f, cy, 3.4f, sc);
            Theme.Label(g, status, Theme.F8_5, Theme.Mix(sc, Color.White, 0.45),
                       new RectangleF(pill.X + 21f, pill.Y, ts.Width + 4f, pillH), Theme.Left);

            float x = pill.Right + 14f;
            string name = _snap != null ? _snap.EffectName : "";
            string hint = _snap != null ? _snap.EffectHint : "";
            Size ns = Theme.Measure(name, Theme.F11B);
            Theme.Label(g, name, Theme.F11B, Theme.Text,
                       new RectangleF(x, cy - 11f, ns.Width + 4f, 22f), Theme.Left);
            x += ns.Width + 11f;
            Theme.Dot(g, x, cy, 1.7f, Theme.A(Theme.TextFaint, 150));
            x += 12f;
            float hintW = _topRect.Right - x - 128f;
            if (hintW > 40f)
                Theme.Label(g, hint, Theme.F8_5, Theme.TextDim,
                           new RectangleF(x, cy - 9f, hintW, 18f), Theme.EllipsisLeft);

            int target = _snap != null ? _snap.TargetFps : 30;
            Theme.Label(g, "目标 " + target.ToString(CultureInfo.InvariantCulture) + " FPS",
                       Theme.M8, Theme.TextFaint,
                       new RectangleF(_topRect.Right - 120f, cy - 9f, 120f, 18f), Theme.Right);
        }

        private void PaintControlsCard(Graphics g)
        {
            Theme.DrawCard(g, _controlsRect, Theme.CardRadius);
            Theme.Label(g, "灯效调节", Theme.F9B, Theme.Text,
                       new RectangleF(_controlsRect.X + 16f, _controlsRect.Y + 11f, 110f, 18f), Theme.Left);
        }

        private void PaintAdminBanner(Graphics g)
        {
            RectangleF r = new RectangleF(_adminRect.X + 0.5f, _adminRect.Y + 0.5f,
                                          _adminRect.Width - 1f, _adminRect.Height - 1f);
            Theme.FillRound(g, r, 9f, Theme.A(Theme.Warn, 18));
            Theme.DrawRound(g, r, 9f, Theme.A(Theme.Warn, 80), 1f);
            Theme.Dot(g, r.X + 21f, r.Y + r.Height / 2f, 3.2f, Theme.Warn);
            Theme.Label(g, "笔记本背光需要管理员权限，重启后才能控制",
                       Theme.F8_5, Theme.Mix(Theme.Warn, Color.White, 0.40),
                       new RectangleF(r.X + 33f, r.Y, r.Width - 205f, r.Height), Theme.EllipsisLeft);
        }

        private void PaintActionRow(Graphics g)
        {
            int total = 0, alive = 0;
            if (_snap != null && _snap.Devices != null)
            {
                total = _snap.Devices.Length;
                for (int i = 0; i < _snap.Devices.Length; i++)
                    if (_snap.Devices[i] != null && _snap.Devices[i].Alive) alive++;
            }
            string online = "在线 " + alive.ToString(CultureInfo.InvariantCulture) + "/" +
                            total.ToString(CultureInfo.InvariantCulture);
            string fps;
            if (_snap != null && _snap.Running)
            {
                double f = _snap.Fps > 0.05 ? _snap.Fps : _snap.TargetFps;
                fps = f.ToString("0.0", CultureInfo.InvariantCulture) + " FPS";
            }
            else
            {
                fps = "已暂停";
            }

            int cy = _actionRect.Y + _actionRect.Height / 2;
            Size s2 = Theme.Measure(online, Theme.F8_5);
            float x2 = _actionRect.Right - s2.Width;
            Theme.Label(g, online, Theme.F8_5, Theme.TextDim,
                       new RectangleF(x2, cy - 9f, s2.Width + 2f, 18f), Theme.Left);
            float dotX = x2 - 15f;
            Theme.Dot(g, dotX, cy, 1.7f, Theme.A(Theme.TextFaint, 160));
            Size s1 = Theme.Measure(fps, Theme.M9);
            float x1 = dotX - 12f - s1.Width;
            Theme.Label(g, fps, Theme.M9, _snap != null && _snap.Running ? Theme.TextDim : Theme.TextFaint,
                       new RectangleF(x1, cy - 9f, s1.Width + 2f, 18f), Theme.Left);
        }

        // --------------------------------------------------------------- interaction
        private void PickColor()
        {
            if (_snap == null || !_snap.EffectUsesColor) return;
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                dlg.Color = Color.FromArgb(_snap.Color.R, _snap.Color.G, _snap.Color.B);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _app.SetColor(new Rgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
            }
        }

        private void RestartElevated()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "--elevated";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                _app.Quit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法申请管理员权限：" + ex.Message, "PrismSync",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space)
            {
                _app.Toggle();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (_app.Settings.CloseToTray)
                {
                    _app.SaveWindowPosition(Left, Top);
                    _app.SaveSettings();
                    Hide();
                }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- lifecycle
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ApplyDarkFrame(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureOnScreen();
            Invalidate();
        }

        private void EnsureOnScreen()
        {
            Rectangle b = Bounds;
            bool visible = false;
            Screen[] screens = Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i].WorkingArea.IntersectsWith(b)) { visible = true; break; }
            }
            if (visible) return;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
        }

        public void ShowFromTray()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(ShowFromTray));
                return;
            }
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            Invalidate();
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Hide to the tray for every close path except an explicit quit. A
            // synthetic WM_CLOSE (automation / taskbar helper) reports
            // CloseReason.TaskManagerClosing rather than UserClosing, so the check
            // must not depend on UserClosing or the process exits on close.
            bool systemExit = e.CloseReason == CloseReason.ApplicationExitCall ||
                              e.CloseReason == CloseReason.WindowsShutDown;
            if (!_forceClose && _app.Settings.CloseToTray && !systemExit)
            {
                e.Cancel = true;
                _app.SaveWindowPosition(Left, Top);
                _app.SaveSettings();
                Hide();
                return;
            }
            _app.SaveWindowPosition(Left, Top);
            _app.SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
