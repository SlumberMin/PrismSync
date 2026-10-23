// Keyboard write-mode lab: the user compares several ways of pushing a STATIC
// rainbow and reports which one is stable (no flicker) and correct.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using PrismSync;

namespace KeyLab
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new LabForm());
        }
    }

    internal sealed class LabForm : Form
    {
        private readonly Label _status;
        private readonly Label _hint;
        private HidDevice _dev;
        private Thread _worker;
        private volatile bool _run;
        private int _mode;
        private long _frames;
        private readonly KeyLayout _layout = new KeyLayout();
        private readonly int[] _ids = new int[68];

        public LabForm()
        {
            Text = "键盘写入方式测试";
            ClientSize = new Size(660, 520);
            BackColor = Color.FromArgb(18, 21, 28);
            ForeColor = Color.FromArgb(230, 234, 242);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            for (int i = 0; i < 68; i++) _ids[i] = _layout.LedId[i];

            Label title = new Label();
            title.Text = "键盘写入方式测试";
            title.Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            title.SetBounds(24, 16, 480, 34);
            Controls.Add(title);

            _hint = new Label();
            _hint.Text = "都是静态彩虹（不流动），所以闪烁只来自写入方式。点一个观察 8 秒，告诉我哪些不闪且颜色正确。";
            _hint.ForeColor = Color.FromArgb(150, 160, 180);
            _hint.SetBounds(26, 52, 620, 36);
            Controls.Add(_hint);

            AddButton("① cmd06 · 30fps", 26, 100, 1);
            AddButton("② cmd06 · 只发一次", 26, 150, 2);
            AddButton("③ cmd08 · 30fps · 68色", 26, 200, 3);
            AddButton("④ cmd08 · 10fps · 68色", 26, 250, 4);
            AddButton("⑤ cmd06 · 纯白 30fps", 26, 300, 5);

            AddButton("⑥ cmd08 · 30fps · ≤16色", 350, 100, 6);
            AddButton("⑦ cmd08 · 30fps · ≤8色", 350, 150, 7);
            AddButton("⑧ cmd08 · 30fps · ≤24色", 350, 200, 8);
            AddButton("⑨ cmd08 · 15fps · ≤16色", 350, 250, 9);
            AddButton("■ 停止 / 全灭", 350, 300, 0);

            _status = new Label();
            _status.ForeColor = Color.FromArgb(90, 200, 140);
            _status.SetBounds(26, 356, 610, 40);
            Controls.Add(_status);

            Label foot = new Label();
            foot.Text = "说明：⑥⑦⑧⑨ 是把彩虹量化成 16/8/24 种颜色后整帧发送，包数少了，固件能完整吃掉整帧。\r\n② 只发一次：如果键盘保持不动，说明固件会自己保持颜色。";
            foot.ForeColor = Color.FromArgb(120, 130, 150);
            foot.SetBounds(26, 400, 610, 70);
            Controls.Add(foot);

            FormClosing += delegate { StopWorker(); CloseDev(); };
            try { OpenDev(); }
            catch (Exception ex) { _status.Text = "打开键盘失败: " + ex.Message; }
        }

        private void AddButton(string text, int x, int y, int mode)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, 280, 38);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(40, 47, 62);
            b.ForeColor = Color.FromArgb(230, 234, 242);
            b.FlatAppearance.BorderColor = Color.FromArgb(66, 76, 96);
            b.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            b.Click += delegate { StartMode(mode); };
            Controls.Add(b);
        }

        private void OpenDev()
        {
            List<HidDeviceInfo> list = HidEnum.Find(0x372E, 0x103E);
            HidDeviceInfo info = null;
            for (int i = 0; i < list.Count; i++) if (list[i].UsagePage == 0xFF60) info = list[i];
            if (info == null) throw new Exception("未找到 0xFF60 厂商接口");
            _dev = HidDevice.Open(info);
            Send(Build(0x01, 0x80, new byte[] { 0x10 }, 1, 0));
            Thread.Sleep(30);
            Send(Build(0x04, 0x02, new byte[] { 0x13, 0x00 }, 1, 0));
            Send(Build(0x04, 0x04, new byte[] { 0x64, 0x04 }, 1, 0));
            Thread.Sleep(40);
        }

        private void CloseDev()
        {
            if (_dev != null) { try { _dev.Dispose(); } catch { } _dev = null; }
        }

        private void StartMode(int mode)
        {
            StopWorker();
            _mode = mode;
            _frames = 0;
            if (mode == 0)
            {
                try { SendCmd06(new Rgb(0, 0, 0)); } catch { }
                _status.Text = "已停止";
                return;
            }
            _run = true;
            _worker = new Thread(delegate ()
            {
                DateTime t0 = DateTime.UtcNow;
                while (_run && (DateTime.UtcNow - t0).TotalSeconds < 8.0)
                {
                    try { SendFrame(_mode); }
                    catch { }
                    _frames++;
                    int wait = 33;
                    if (_mode == 4 || _mode == 9) wait = 66;
                    if (_mode == 2) wait = 600;
                    Thread.Sleep(wait);
                    if (_mode == 2) _run = false;
                }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        _status.Text = "已结束：模式 " + _mode + "，发送 " + _frames + " 帧";
                    }));
                }
                catch { }
            });
            _worker.IsBackground = true;
            _worker.Start();
            string[] names = new string[] { "停止", "① cmd06 30fps", "② cmd06 只发一次",
                "③ cmd08 30fps 68色", "④ cmd08 10fps 68色", "⑤ cmd06 纯白",
                "⑥ cmd08 30fps ≤16色", "⑦ cmd08 30fps ≤8色", "⑧ cmd08 30fps ≤24色",
                "⑨ cmd08 15fps ≤16色" };
            _status.Text = "运行中：" + names[Math.Min(mode, names.Length - 1)] + "（8 秒）";
        }

        private void StopWorker()
        {
            _run = false;
            Thread w = _worker;
            _worker = null;
            if (w != null) { try { w.Join(400); } catch { } }
        }

        private void SendFrame(int mode)
        {
            if (_dev == null) return;
            if (mode == 5) { SendCmd06(new Rgb(255, 255, 255)); return; }
            if (mode == 1 || mode == 2) { SendCmd06Gradient(); return; }
            if (mode >= 6)
            {
                int max = mode == 7 ? 8 : (mode == 8 ? 24 : 16);
                SendCmd08Quantized(max);
                return;
            }
            SendCmd08Gradient();
        }

        private static Rgb Rainbow(int i, int total)
        {
            return Rgb.FromHsv(0.15 + (i / (double)(total - 1)) * 2.5, 1.0, 1.0);
        }

        private void SendCmd06(Rgb color)
        {
            const int packets = 7;
            for (int p = 0; p < packets; p++)
            {
                int start = p * 11;
                int n = Math.Min(11, 68 - start);
                byte[] payload = new byte[5 * n];
                for (int j = 0; j < n; j++)
                {
                    int id = _ids[start + j];
                    int o = j * 5;
                    payload[o] = (byte)(id >> 8);
                    payload[o + 1] = (byte)id;
                    payload[o + 2] = color.R; payload[o + 3] = color.G; payload[o + 4] = color.B;
                }
                Send(Build(0x06, 0x00, payload, packets, p));
            }
            Send(Build(0x01, 0x00, null, 1, 0));
        }

        private void SendCmd06Gradient()
        {
            const int packets = 7;
            for (int p = 0; p < packets; p++)
            {
                int start = p * 11;
                int n = Math.Min(11, 68 - start);
                byte[] payload = new byte[5 * n];
                for (int j = 0; j < n; j++)
                {
                    int i = start + j;
                    int id = _ids[i];
                    Rgb c = Rainbow(i, 68);
                    int o = j * 5;
                    payload[o] = (byte)(id >> 8);
                    payload[o + 1] = (byte)id;
                    payload[o + 2] = c.R; payload[o + 3] = c.G; payload[o + 4] = c.B;
                }
                Send(Build(0x06, 0x00, payload, packets, p));
            }
            Send(Build(0x01, 0x00, null, 1, 0));
        }

        private void SendCmd08Gradient()
        {
            SendCmd08(0);
        }

        private void SendCmd08Quantized(int maxColors)
        {
            int steps = 24;
            while (steps > 2 && CountColors(steps) > maxColors) steps -= 2;
            SendCmd08(steps);
        }

        private int CountColors(int hueSteps)
        {
            int seen = 0;
            int[] colors = new int[68];
            for (int i = 0; i < 68; i++)
            {
                Rgb c = Rgb.QuantizeHsv(Rainbow(i, 68), hueSteps, 2, 8);
                int key = (c.R << 16) | (c.G << 8) | c.B;
                bool found = false;
                for (int j = 0; j < seen; j++) if (colors[j] == key) { found = true; break; }
                if (!found) colors[seen++] = key;
            }
            return seen;
        }

        private void SendCmd08(int hueSteps)
        {
            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < 68; i++)
            {
                Rgb c = hueSteps > 0 ? Rgb.QuantizeHsv(Rainbow(i, 68), hueSteps, 2, 8) : Rainbow(i, 68);
                int key = (c.R << 16) | (c.G << 8) | c.B;
                List<int> l;
                if (!groups.TryGetValue(key, out l)) { l = new List<int>(); groups[key] = l; }
                l.Add(_ids[i]);
            }
            List<byte[]> packets = new List<byte[]>();
            foreach (KeyValuePair<int, List<int>> kv in groups)
            {
                int color = kv.Key;
                List<int> ids = kv.Value;
                for (int s = 0; s < ids.Count; s += 52)
                {
                    int n = Math.Min(52, ids.Count - s);
                    byte[] payload = new byte[4 + n];
                    payload[0] = (byte)((color >> 16) & 0xFF);
                    payload[1] = (byte)((color >> 8) & 0xFF);
                    payload[2] = (byte)(color & 0xFF);
                    payload[3] = (byte)n;
                    for (int i = 0; i < n; i++) payload[4 + i] = (byte)ids[s + i];
                    packets.Add(payload);
                }
            }
            for (int i = 0; i < packets.Count; i++)
                Send(Build(0x08, 0x01, packets[i], packets.Count, i));
            Send(Build(0x01, 0x00, null, 1, 0));
        }

        private void Send(byte[] packet)
        {
            if (_dev != null) _dev.Write(packet, 64);
        }

        private static byte[] Build(byte cmd, byte param, byte[] payload, int total, int index)
        {
            byte[] p = new byte[64];
            p[0] = 0x09; p[1] = cmd; p[2] = param; p[4] = (byte)total; p[5] = (byte)index;
            if (payload != null && payload.Length > 0)
            {
                int n = Math.Min(payload.Length, 56);
                Array.Copy(payload, 0, p, 7, n);
                p[6] = (byte)n;
            }
            int sum = 0;
            for (int i = 0; i < 63; i++) sum += p[i];
            p[63] = (byte)((0xFF - (sum & 0xFF)) & 0xFF);
            return p;
        }
    }
}
