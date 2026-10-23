// 狼蛛 HERO 68 HE 灯位校准工具（独立原生小程序，给用户逐个按键使用）。
//
// 流程：工具逐个点亮 67 颗 LED 中的一颗（白色，其余全灭），用户按下正在亮的
// 那个键；工具用全局键盘钩子读到 HID usage，记录 "LED id -> 键"，结束后把
// native/keymap/keymap.json 写好，主程序编译时把它嵌进 exe，键位与预览就一
// 一对应了。F1 跳过、F2 重做上一个、F3 保存退出。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PrismSync;

namespace KeyCal
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CalForm());
        }
    }

    internal sealed class CalKeyboard : IDisposable
    {
        private HidDevice _dev;
        private readonly int[] _ids;

        public CalKeyboard(int[] ids) { _ids = ids; }

        public bool IsOpen { get { return _dev != null; } }

        public bool Open(out string error)
        {
            error = "";
            try
            {
                HidDeviceInfo found = null;
                List<HidDeviceInfo> list = HidEnum.Find(0x372E, 0x103E);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].UsagePage == 0xFF60) { found = list[i]; break; }
                if (found == null)
                {
                    error = "没找到键盘的厂商接口（请确认键盘已插好）";
                    return false;
                }
                _dev = HidDevice.Open(found);
                Send(Build(0x01, 0x80, new byte[] { 0x10 }, 1, 0));
                Thread.Sleep(30);
                Send(Build(0x04, 0x02, new byte[] { 0x13, 0x00 }, 1, 0));
                Send(Build(0x04, 0x04, new byte[] { 0x64, 0x04 }, 1, 0));
                Thread.Sleep(40);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void ShowOnly(int ledId)
        {
            if (_dev == null) return;
            int others = _ids.Length - 1;
            int packets = 1 + (others + 51) / 52;
            int idx = 0;

            byte[] white = new byte[5];
            white[0] = 255; white[1] = 255; white[2] = 255; white[3] = 1; white[4] = (byte)ledId;
            Send(Build(0x08, 0x01, white, packets, idx++));

            List<int> rest = new List<int>(others);
            for (int i = 0; i < _ids.Length; i++) if (_ids[i] != ledId) rest.Add(_ids[i]);
            for (int start = 0; start < rest.Count; start += 52)
            {
                int n = Math.Min(52, rest.Count - start);
                byte[] payload = new byte[4 + n];
                payload[3] = (byte)n;
                for (int i = 0; i < n; i++) payload[4 + i] = (byte)rest[start + i];
                Send(Build(0x08, 0x01, payload, packets, idx++));
            }
            Send(Build(0x01, 0x00, null, 1, 0));
        }

        public void AllOff()
        {
            if (_dev == null) return;
            for (int start = 0; start < _ids.Length; start += 52)
            {
                int n = Math.Min(52, _ids.Length - start);
                byte[] payload = new byte[4 + n];
                payload[3] = (byte)n;
                for (int i = 0; i < n; i++) payload[4 + i] = (byte)_ids[start + i];
                int total = (_ids.Length + 51) / 52;
                Send(Build(0x08, 0x01, payload, total, start / 52));
            }
            Send(Build(0x01, 0x00, null, 1, 0));
        }

        private void Send(byte[] packet)
        {
            if (_dev == null) return;
            _dev.Write(packet, 64);
        }

        private static byte[] Build(byte cmd, byte param, byte[] payload, int total, int index)
        {
            byte[] p = new byte[64];
            p[0] = 0x09;
            p[1] = cmd;
            p[2] = param;
            p[4] = (byte)total;
            p[5] = (byte)index;
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

        public void Dispose()
        {
            if (_dev != null) { try { _dev.Dispose(); } catch { } _dev = null; }
        }
    }

    internal sealed class CalForm : Form
    {
        // ---- palette -----------------------------------------------------
        private static readonly Color Bg = Color.FromArgb(11, 13, 18);
        private static readonly Color Card = Color.FromArgb(24, 29, 39);
        private static readonly Color Border = Color.FromArgb(44, 51, 68);
        private static readonly Color TextColor = Color.FromArgb(232, 236, 244);
        private static readonly Color Dim = Color.FromArgb(140, 150, 170);
        private static readonly Color Accent = Color.FromArgb(77, 163, 255);
        private static readonly Color Accent2 = Color.FromArgb(180, 92, 255);
        private static readonly Color Good = Color.FromArgb(53, 208, 127);
        private static readonly Color Warn = Color.FromArgb(255, 184, 77);

        private static readonly int[] LedIds = BuildIds();

        // ---- state -------------------------------------------------------
        private readonly int[] _ids = LedIds;
        private readonly Dictionary<int, int> _map = new Dictionary<int, int>(); // ledId -> usage
        private readonly List<string> _log = new List<string>();
        private int _index;
        private bool _running;
        private bool _awaiting;
        private CalKeyboard _kbd;
        private IntPtr _hook;
        private LowLevelProc _hookProc;
        private readonly System.Windows.Forms.Timer _repaint;

        // controls
        private readonly Label _stepLabel;
        private readonly Label _promptLabel;
        private readonly Label _ledLabel;
        private readonly Label _hintLabel;
        private readonly Label _statusLabel;
        private readonly Panel _logPanel;
        private readonly RoundedButton _startButton;
        private readonly RoundedButton _skipButton;
        private readonly RoundedButton _undoButton;
        private readonly RoundedButton _saveButton;

        public CalForm()
        {
            Text = "键盘灯位校准 · HERO 68 HE";
            ClientSize = new Size(760, 660);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = TextColor;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 9f);

            _stepLabel = MakeLabel("", 18f, true, TextColor);
            _stepLabel.SetBounds(36, 110, 500, 40);
            _promptLabel = MakeLabel("把键盘放在面前，点「开始校准」", 13f, false, Dim);
            _promptLabel.SetBounds(36, 152, 688, 34);
            _ledLabel = MakeLabel("", 9f, false, Dim);
            _ledLabel.SetBounds(36, 190, 500, 24);

            _startButton = MakeButton("开始校准", Accent, Color.White, 150);
            _startButton.SetBounds(36, 560, 150, 44);
            _startButton.Click += delegate { StartOrRestart(); };

            _skipButton = MakeButton("跳过 (F1)", Card, TextColor, 130);
            _skipButton.SetBounds(198, 560, 130, 44);
            _skipButton.Enabled = false;
            _skipButton.Click += delegate { Skip(); };

            _undoButton = MakeButton("重做上一个 (F2)", Card, TextColor, 160);
            _undoButton.SetBounds(340, 560, 160, 44);
            _undoButton.Enabled = false;
            _undoButton.Click += delegate { Undo(); };

            _saveButton = MakeButton("保存并退出 (F3)", Card, TextColor, 160);
            _saveButton.SetBounds(512, 560, 160, 44);
            _saveButton.Click += delegate { SaveAndExit(); };

            _hintLabel = MakeLabel("点亮的键按下即可自动记录；如果亮的是灯箱/logo 不是按键，按 F1 跳过。校准期间键盘输入会被本工具拦截。",
                                   9f, false, Dim);
            _hintLabel.SetBounds(36, 616, 700, 24);

            _statusLabel = MakeLabel("", 9f, false, Dim);
            _statusLabel.SetBounds(36, 80, 688, 22);

            _logPanel = new Panel();
            _logPanel.SetBounds(36, 256, 688, 286);
            _logPanel.BackColor = Bg;
            _logPanel.Paint += delegate (object s, PaintEventArgs e) { DrawLogs(e.Graphics); };

            Controls.AddRange(new Control[] { _stepLabel, _promptLabel, _ledLabel, _startButton,
                _skipButton, _undoButton, _saveButton, _hintLabel, _statusLabel, _logPanel });

            _repaint = new System.Windows.Forms.Timer();
            _repaint.Interval = 120;
            _repaint.Tick += delegate { Refresh(); };
            _repaint.Start();

            FormClosing += delegate { try { if (_map.Count > 0) Save(true); } catch { } Cleanup(); };
        }

        private static int[] BuildIds()
        {
            List<int> list = new List<int>();
            list.Add(1);
            // HERO 68 HE really is a 68-key board: LED id 76 (0x4C) is part of the
            // device's own group report but was missing from the old 67-key list.
            for (int i = 15; i <= 77; i++) list.Add(i);
            list.Add(98); list.Add(99); list.Add(102); list.Add(103);
            list.Sort();
            return list.ToArray();
        }

        private Label MakeLabel(string text, float size, bool bold, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            l.ForeColor = color;
            l.BackColor = Color.Transparent;
            return l;
        }

        private RoundedButton MakeButton(string text, Color back, Color fore, int width)
        {
            RoundedButton b = new RoundedButton();
            b.Text = text;
            b.SetBounds(0, 0, width, 44);
            b.BackColor = back;
            b.ForeColor = fore;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            b.TabStop = false;
            return b;
        }

        // ---- lifecycle ---------------------------------------------------
        private void StartOrRestart()
        {
            if (_running) return;
            KillMHub();
            string error;
            _kbd = new CalKeyboard(_ids);
            if (!_kbd.Open(out error))
            {
                _statusLabel.Text = "打开键盘失败: " + error;
                _statusLabel.ForeColor = Warn;
                _kbd.Dispose();
                _kbd = null;
                return;
            }
            if (_hook == IntPtr.Zero)
            {
                _hookProc = HookCallback;
                _hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _hookProc, GetModuleHandle(null), 0);
            }
            _map.Clear();
            _log.Clear();
            _index = 0;
            _running = true;
            _startButton.Text = "校准中…";
            _startButton.BackColor = Color.FromArgb(40, 48, 64);
            _skipButton.Enabled = true;
            _undoButton.Enabled = true;
            _statusLabel.ForeColor = Dim;
            _statusLabel.Text = "已开始。每颗灯只亮一颗，按键盘上对应的那个键。";
            LightCurrent();
        }

        private void LightCurrent()
        {
            if (!_running) return;
            if (_index >= _ids.Length) { Finish(); return; }
            int id = _ids[_index];
            _awaiting = true;
            try { _kbd.ShowOnly(id); } catch { }
            _stepLabel.Text = "第 " + (_index + 1) + " / " + _ids.Length + " 个灯";
            _promptLabel.Text = "请按一下正在亮起的那个键";
            _promptLabel.ForeColor = TextColor;
            _ledLabel.Text = "LED id = " + id;
            Refresh();
        }

        private void Skip()
        {
            if (!_running) return;
            int id = _ids[_index];
            _map.Remove(id);
            _log.Add("LED " + id + "  →  已跳过");
            if (_log.Count > 60) _log.RemoveAt(0);
            Next();
        }

        private void Undo()
        {
            if (!_running || _index == 0) return;
            _index--;
            int id = _ids[_index];
            _map.Remove(id);
            _log.Add("LED " + id + "  →  重做");
            if (_log.Count > 60) _log.RemoveAt(0);
            LightCurrent();
        }

        private void Next()
        {
            _awaiting = false;
            _index++;
            if (_index >= _ids.Length) { Finish(); return; }
            LightCurrent();
        }

        private void Finish()
        {
            _running = false;
            _awaiting = false;
            try { if (_kbd != null) _kbd.AllOff(); } catch { }
            _startButton.Text = "重新校准";
            _startButton.BackColor = Accent;
            _skipButton.Enabled = false;
            _undoButton.Enabled = false;
            _stepLabel.Text = "校准完成";
            _promptLabel.Text = "已记录 " + _map.Count + " 个灯位，点「保存并退出」";
            _promptLabel.ForeColor = Good;
            _ledLabel.Text = "";
            Save(false);
            _statusLabel.ForeColor = Good;
            _statusLabel.Text = "已保存到 native\\keymap\\keymap.json，主程序下次编译会自动嵌入。";
            Refresh();
        }

        private void SaveAndExit()
        {
            Save(false);
            Cleanup();
            Close();
        }

        private void Cleanup()
        {
            _running = false;
            _awaiting = false;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            if (_kbd != null)
            {
                try { _kbd.Dispose(); } catch { }
                _kbd = null;
            }
            if (_repaint.Enabled) _repaint.Stop();
        }

        private void OnVk(int vk)
        {
            if (!_running) return;
            if (vk == 0x70) { Skip(); return; }       // F1
            if (vk == 0x71) { Undo(); return; }       // F2
            if (vk == 0x72) { SaveAndExit(); return; }// F3
            if (!_awaiting) return;

            int usage = VkToUsage(vk);
            if (usage == 0)
            {
                _promptLabel.Text = "这个键不上报标准键值（可能是 Fn/组合键），按 F1 跳过";
                _promptLabel.ForeColor = Warn;
                return;
            }
            int id = _ids[_index];
            _map[id] = usage;
            Save(true);   // autosave after every key: a crash never loses progress
            int row; double x, w;
            string extra = "";
            if (UsageLayout.TryLayout(usage, out row, out x, out w))
                extra = "  第 " + (row + 1) + " 行 x=" + x.ToString("0.00");
            _log.Add("LED " + id + "  →  " + UsageLayout.Name(usage) + "  (usage 0x" + usage.ToString("X2") + ")" + extra);
            if (_log.Count > 60) _log.RemoveAt(0);
            _promptLabel.Text = "已记录，继续下一个…";
            _promptLabel.ForeColor = Good;
            _awaiting = false;
            _index++;
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 220;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                if (_running) LightCurrent();
            };
            t.Start();
        }

        private static int VkToUsage(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return 0x04 + (vk - 0x41);
            if (vk >= 0x30 && vk <= 0x39) return 0x1E + (vk - 0x30);
            switch (vk)
            {
                case 0x0D: return 0x28;
                case 0x1B: return 0x29;
                case 0x08: return 0x2A;
                case 0x09: return 0x2B;
                case 0x20: return 0x2C;
                case 0xBA: return 0x33;
                case 0xBB: return 0x2E;
                case 0xBC: return 0x36;
                case 0xBD: return 0x2D;
                case 0xBE: return 0x37;
                case 0xBF: return 0x38;
                case 0xC0: return 0x35;
                case 0xDB: return 0x2F;
                case 0xDC: return 0x31;
                case 0xDD: return 0x30;
                case 0xDE: return 0x34;
                case 0x14: return 0x39;
                case 0x25: return 0x50;
                case 0x26: return 0x52;
                case 0x27: return 0x4F;
                case 0x28: return 0x51;
                case 0xA0: return 0xE1;
                case 0xA1: return 0xE5;
                case 0xA2: return 0xE0;
                case 0xA3: return 0xE4;
                case 0xA4: return 0xE2;
                case 0xA5: return 0xE6;
                case 0x5B: return 0xE3;
                case 0x5C: return 0xE7;
                case 0x24: return 0x4A;
                case 0x23: return 0x4D;
                case 0x21: return 0x4B;
                case 0x22: return 0x4E;
                case 0x2D: return 0x49;
                case 0x2E: return 0x4C;
            }
            return 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x0100 || msg == 0x0104)   // WM_KEYDOWN / WM_SYSKEYDOWN
                {
                    int vk = Marshal.ReadInt32(lParam);
                    try { BeginInvoke(new Action<int>(OnVk), vk); } catch { }
                    return new IntPtr(1);              // swallow while calibrating
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // ---- saving ------------------------------------------------------
        private void Save(bool quiet)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string keymapDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "keymap"));
                try { if (!Directory.Exists(keymapDir)) Directory.CreateDirectory(keymapDir); }
                catch
                {
                    keymapDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "PrismSync");
                    Directory.CreateDirectory(keymapDir);
                }

                StringBuilder sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"uid\": \"110000000003\",\n");
                sb.Append("  \"source\": \"manual calibration (keycal)\",\n");
                sb.Append("  \"created\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\",\n");
                sb.Append("  \"keys\": [\n");
                List<int> sorted = new List<int>(_map.Keys);
                sorted.Sort();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int id = sorted[i];
                    int usage = _map[id];
                    int row = 0; double x = (i + 0.5) / Math.Max(1, sorted.Count); double w = 1;
                    UsageLayout.TryLayout(usage, out row, out x, out w);
                    sb.Append("    {\"id\": ").Append(id)
                      .Append(", \"usage\": ").Append(usage)
                      .Append(", \"row\": ").Append(row)
                      .Append(", \"x\": ").Append(x.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(", \"width\": ").Append(w.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                      .Append("}");
                    if (i < sorted.Count - 1) sb.Append(",");
                    sb.Append("\n");
                }
                sb.Append("  ]\n");
                sb.Append("}\n");

                string path = Path.Combine(keymapDir, "keymap.json");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

                StringBuilder raw = new StringBuilder();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int id = sorted[i];
                    int usage = _map[id];
                    raw.Append("id=").Append(id)
                       .Append(" usage=0x").Append(usage.ToString("X2"))
                       .Append(" key=").Append(UsageLayout.Name(usage)).Append("\n");
                }
                File.WriteAllText(Path.Combine(keymapDir, "calibration_raw.txt"), raw.ToString(), new UTF8Encoding(false));
                if (!quiet) _statusLabel.Text = "已保存: " + path;
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "保存失败: " + ex.Message;
                _statusLabel.ForeColor = Warn;
            }
        }

        private static void KillMHub()
        {
            try
            {
                System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcesses();
                for (int i = 0; i < ps.Length; i++)
                {
                    string n = ps[i].ProcessName;
                    if (n.IndexOf("MCHOSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("M HUB", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { ps[i].Kill(); } catch { }
                    }
                }
            }
            catch { }
        }

        // ---- painting ----------------------------------------------------
        private void DrawLogs(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = ThemeRounded(new RectangleF(0, 0, _logPanel.Width - 1, _logPanel.Height - 1), 14))
            using (SolidBrush b = new SolidBrush(Card)) g.FillPath(b, p);
            using (GraphicsPath p = ThemeRounded(new RectangleF(0, 0, _logPanel.Width - 1, _logPanel.Height - 1), 14))
            using (Pen pen = new Pen(Border)) g.DrawPath(pen, p);

            int show = Math.Min(10, _log.Count);
            int y = 14;
            using (Font f = new Font("Microsoft YaHei UI", 9f))
            using (Font fb = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, _map.Count + " / " + _ids.Length + " 已记录", fb,
                    new Point(16, 10), TextColor);
                for (int i = _log.Count - show; i < _log.Count; i++)
                {
                    if (i < 0) continue;
                    y += 26;
                    TextRenderer.DrawText(g, _log[i], f, new Point(16, y), Dim);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // header
            using (Font f = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold))
                TextRenderer.DrawText(g, "键盘灯位校准", f, new Point(34, 28), TextColor);
            using (Font f = new Font("Microsoft YaHei UI", 9f))
                TextRenderer.DrawText(g, "狼蛛 HERO 68 HE · 逐个点亮，按一下亮着的那个键", f,
                    new Point(36, 62), Dim);

            // status card
            RectangleF card = new RectangleF(24, 100, ClientSize.Width - 48, 146);
            using (GraphicsPath p = ThemeRounded(card, 16))
            using (SolidBrush b = new SolidBrush(Card)) g.FillPath(b, p);
            using (GraphicsPath p = ThemeRounded(card, 16))
            using (Pen pen = new Pen(Border)) g.DrawPath(pen, p);

            // progress
            float ratio = _ids.Length == 0 ? 0 : (float)_index / _ids.Length;
            RectangleF bar = new RectangleF(card.X + 16, card.Bottom - 22, card.Width - 32, 6);
            using (GraphicsPath p = ThemeRounded(bar, 3))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 47, 62))) g.FillPath(b, p);
            if (ratio > 0)
            {
                using (GraphicsPath p = ThemeRounded(new RectangleF(bar.X, bar.Y, Math.Max(4, bar.Width * ratio), bar.Height), 3))
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new RectangleF(bar.X, bar.Y - 1, Math.Max(8, bar.Width * ratio), bar.Height + 2),
                    Accent, Accent2, LinearGradientMode.Horizontal))
                    g.FillPath(b, p);
            }
        }

        private static GraphicsPath ThemeRounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---- win32 -------------------------------------------------------
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr mod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
    }

    internal sealed class RoundedButton : Button
    {
        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            GraphicsPath p = new GraphicsPath();
            float d = 12;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            using (SolidBrush b = new SolidBrush(Enabled ? BackColor : Color.FromArgb(30, 35, 46)))
                g.FillPath(b, p);
            using (Pen pen = new Pen(Enabled ? Color.FromArgb(58, 66, 86) : Color.FromArgb(38, 44, 58)))
                g.DrawPath(pen, p);
            p.Dispose();
            TextRenderer.DrawText(g, Text, Font, ClientRectangle,
                Enabled ? ForeColor : Color.FromArgb(100, 108, 124),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
