// PrismSync native app - Clevo WMI laptop keyboard backlight (Colorful X15 AT23 / NP50SNE).
//
// root\WMI : CLEVO_GET : SetKBLED(uint32)
//   light on    0xE007F007
//   part left   0xF0000000   (this model is one global colour anyway)
//   brightness  0xF4000000 | level (0 / 63 / 127 / 255)
//   colour      (B << 16) + (R << 8) + G
// A SetKBLED round trip is milliseconds, far slower than a frame, so the newest
// colour goes into a single slot and a background thread drains it - intermediate
// frames are dropped on purpose.
using System;
using System.Management;
using System.Threading;

namespace PrismSync
{
    public sealed class ClevoDevice : IDeviceAdapter
    {
        private const uint PartLeft = 0xF0000000;
        private const uint Brightness = 0xF4000000;
        private const uint LevelHigh = 255;
        private const uint LightOn = 0xE007F007;

        private readonly object _lock = new object();
        private Thread _writer;
        private volatile bool _run;
        private volatile bool _open;
        private uint? _pending;
        private uint _shown;
        private bool _hasShown;
        private volatile string _lastError = "";
        private long _applied;
        private long _failed;
        private int _consecutiveFailures;

        public string Id { get { return "laptop"; } }
        public string Name { get { return "笔记本"; } }
        public string Detail { get { return "X15 AT23 键盘背光"; } }
        public bool SingleZone { get { return true; } }
        public bool IsOpen { get { return _open; } }
        public string LastError { get { lock (_lock) { return _lastError; } } }
        public long Applied { get { return Interlocked.Read(ref _applied); } }
        public long Failed { get { return Interlocked.Read(ref _failed); } }
        public string Status { get { return ""; } }

        public bool Open()
        {
            Close();
            if (!AdminCheck.IsAdmin())
            {
                SetError("需要管理员权限重启应用");
                return false;
            }
            _run = true;
            _open = false;
            _lastError = "";
            _hasShown = false;
            _writer = new Thread(WriterLoop);
            _writer.IsBackground = true;
            _writer.Name = "clevo-wmi";
            _writer.Start();

            for (int i = 0; i < 120; i++)
            {
                if (_open) return true;
                if (!string.IsNullOrEmpty(_lastError)) break;
                Thread.Sleep(40);
            }
            return _open;
        }

        private void WriterLoop()
        {
            ManagementObject mo = null;
            try
            {
                using (ManagementObjectSearcher searcher =
                       new ManagementObjectSearcher("root\\WMI", "select * from CLEVO_GET"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject o in results) { mo = o; break; }
                }
                if (mo == null)
                {
                    SetError("未找到 CLEVO_GET（先以管理员运行 register_clevo.ps1 注册）");
                    return;
                }
                _open = true;
                SetError("");
                TryInvoke(mo, Brightness | LevelHigh);
                TryInvoke(mo, LightOn);

                while (_run)
                {
                    uint? value = null;
                    lock (_lock) { value = _pending; _pending = null; }
                    if (value == null) { Thread.Sleep(2); continue; }
                    if (_hasShown && value.Value == _shown) continue;
                    try
                    {
                        mo.InvokeMethod("SetKBLED", new object[] { value.Value });
                        _shown = value.Value;
                        _hasShown = true;
                        _consecutiveFailures = 0;
                        Interlocked.Increment(ref _applied);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _failed);
                        if (++_consecutiveFailures > 30)
                        {
                            SetError("Clevo WMI 写入失败: " + ex.Message);
                            break;
                        }
                    }
                }
            }
            catch (ManagementException ex)
            {
                SetError(Explain(ex));
            }
            catch (Exception ex)
            {
                SetError("笔记本背光不可用: " + ex.Message);
            }
            finally
            {
                _open = false;
                if (mo != null) { try { mo.Dispose(); } catch { } }
            }
        }

        private static void TryInvoke(ManagementObject mo, uint value)
        {
            try { mo.InvokeMethod("SetKBLED", new object[] { value }); } catch { }
        }

        private static string Explain(ManagementException ex)
        {
            string msg = ex.Message;
            if (msg.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 ||
                msg.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0)
                return "需要管理员权限重启应用";
            return "Clevo WMI 不可用: " + msg;
        }

        private void SetError(string msg)
        {
            lock (_lock) { _lastError = msg; }
        }

        public void Apply(Frame frame)
        {
            if (!_open) return;
            Rgb c = frame.Laptop;
            uint value = PartLeft | (uint)((c.B << 16) | (c.R << 8) | c.G);
            lock (_lock) { _pending = value; }
        }

        public void Close()
        {
            _run = false;
            Thread t = _writer;
            _writer = null;
            if (t != null) { try { t.Join(600); } catch { } }
            _open = false;
            lock (_lock) { _pending = null; }
        }

        public void NoteError(string message)
        {
            SetError(message);
        }
    }
}
