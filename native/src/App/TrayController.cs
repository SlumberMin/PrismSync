// PrismSync native app - tray icon: close-to-tray, show / pause / exit.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PrismSync.UI;

namespace PrismSync
{
    public sealed class TrayController : IDisposable
    {
        private readonly MainForm _form;
        private readonly AppController _app;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _toggle;
        private readonly ToolStripMenuItem _show;
        private readonly Timer _poll;
        private bool _balloonShown;
        private bool _disposed;
        private IntPtr _iconHandle = IntPtr.Zero;

        public TrayController(MainForm form, AppController app)
        {
            _form = form;
            _app = app;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.BackColor = Color.FromArgb(26, 31, 42);
            _menu.ForeColor = Color.FromArgb(230, 234, 242);
            _menu.RenderMode = ToolStripRenderMode.System;

            _show = new ToolStripMenuItem("显示主界面");
            _show.Click += delegate { form.ShowFromTray(); };
            _menu.Items.Add(_show);

            _toggle = new ToolStripMenuItem("启动灯效");
            _toggle.Click += delegate
            {
                app.Toggle();
                UpdateState();
            };
            _menu.Items.Add(_toggle);

            ToolStripMenuItem hub = new ToolStripMenuItem("关闭 M HUB（避免抢灯）");
            hub.Click += delegate { app.KillMHub(false); };
            _menu.Items.Add(hub);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { app.Quit(); };
            _menu.Items.Add(exit);

            _icon = new NotifyIcon();
            _icon.ContextMenuStrip = _menu;
            _icon.Text = "棱镜同步 PrismSync";
            _icon.Icon = LoadIcon();
            _icon.Visible = true;
            _icon.DoubleClick += delegate { form.ShowFromTray(); };
            _icon.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) form.ShowFromTray();
            };

            form.VisibleChanged += delegate
            {
                if (!form.Visible) ShowTrayHint();
            };

            _poll = new Timer();
            _poll.Interval = 500;
            _poll.Tick += delegate { UpdateState(); };
            _poll.Start();
        }

        private Icon LoadIcon()
        {
            try
            {
                Icon ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ico != null) return ico;
            }
            catch { }

            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(20, 24, 33)))
                        g.FillEllipse(bg, 1, 1, 30, 30);
                    using (Pen p = new Pen(Color.FromArgb(77, 163, 255), 2.4f))
                        g.DrawArc(p, 5, 5, 22, 22, 150, 240);
                    using (Pen p = new Pen(Color.FromArgb(180, 92, 255), 2.4f))
                        g.DrawArc(p, 5, 5, 22, 22, 30, 110);
                    using (Pen p = new Pen(Color.FromArgb(53, 208, 127), 2.4f))
                        g.DrawArc(p, 5, 5, 22, 22, -80, 100);
                }
                _iconHandle = bmp.GetHicon();
                return Icon.FromHandle(_iconHandle);
            }
        }

        private void UpdateState()
        {
            AppSnapshot s;
            try { s = _app.Snapshot(); } catch { return; }
            _toggle.Text = s.Running ? "暂停灯效" : "启动灯效";
            string tip = "PrismSync · " + s.EffectName;
            if (s.Running)
            {
                tip += "  " + s.Fps.ToString("0") + " fps";
                if (s.Devices != null)
                {
                    int alive = 0;
                    for (int i = 0; i < s.Devices.Length; i++) if (s.Devices[i].Alive) alive++;
                    tip += "  · " + alive + " 台在线";
                }
            }
            else tip += "  · 已暂停";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (_icon.Text != tip) _icon.Text = tip;
        }

        private void ShowTrayHint()
        {
            if (_balloonShown) return;
            _balloonShown = true;
            try
            {
                _icon.BalloonTipTitle = "PrismSync 仍在运行";
                _icon.BalloonTipText = "窗口已隐藏到托盘，灯效继续。右键托盘图标可暂停或退出。";
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.ShowBalloonTip(2500);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _poll.Stop(); _poll.Dispose(); } catch { }
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            if (_iconHandle != IntPtr.Zero)
            {
                try { DestroyIcon(_iconHandle); } catch { }
                _iconHandle = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
