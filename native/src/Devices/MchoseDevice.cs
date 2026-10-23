// PrismSync native app - MCHOSE V9 Turbo+ headset lighting.
//
// 64-byte wire packet, first byte is report id 0xAA:
//   AA 27 00 00 00 0A 01 <mode> 05 <dir> <bright> 00 <R> <G> <B> ...
// Windows requires HidD_SetOutputReport with a 65-byte buffer = [0x00] + wire.
//
// The device answers every output report with an input report. Those ACKs MUST be
// drained on the same handle, otherwise the firmware silently stops accepting
// frames while every API call still returns success. A dedicated reader thread
// keeps the queue empty.
using System;
using System.Threading;

namespace PrismSync
{
    public sealed class MchoseDevice : IDeviceAdapter
    {
        public const int VendorId = 0x3837;
        public const int ProductId = 0x6008;
        public const int UsagePage = 0xFF00;
        public const byte ReportId = 0xAA;

        private const double MinInterval = 0.05;   // firmware renders in its own time
        private readonly object _lock = new object();
        private HidDevice _hid;
        private Thread _reader;
        private volatile bool _readerRun;
        private bool _open;
        private int _mode;
        private byte _brightness = 0x03;
        private byte _direction;
        private double _lastSent;
        private Rgb _lastRgb;
        private bool _hasLast;
        private long _applied;
        private long _failed;
        private string _lastError = "";
        private int _ackCount;
        private volatile int _probeR;
        private volatile int _probeG;
        private volatile int _probeB;
        private volatile bool _hasProbe;

        public MchoseDevice(int mode)
        {
            _mode = mode;
            _lastRgb = Rgb.Black;
            _lastSent = -1;
        }

        public string Id { get { return "headset"; } }
        public string Name { get { return "耳机"; } }
        public string Detail { get { return "MCHOSE V9 Turbo+"; } }
        public bool SingleZone { get { return true; } }
        public bool IsOpen { get { lock (_lock) { return _open; } } }
        public string LastError { get { lock (_lock) { return _lastError; } } }
        public long Applied { get { return Interlocked.Read(ref _applied); } }
        public long Failed { get { return Interlocked.Read(ref _failed); } }
        public int Acks { get { return _ackCount; } }

        public string Status
        {
            get
            {
                int m;
                lock (_lock) { m = _mode; }
                if (m == 0) return "";
                string[] names = new string[] { "常亮", "呼吸", "繁星", "循环", "随波" };
                return "固件模式: " + (m < names.Length ? names[m] : m.ToString());
            }
        }

        public int Mode
        {
            get { lock (_lock) { return _mode; } }
            set
            {
                lock (_lock) { _mode = value; }
                _hasLast = false;
                if (IsOpen) SendCurrent(Rgb.White, true);
            }
        }

        public bool Open()
        {
            Close();
            try
            {
                HidDeviceInfo found = null;
                System.Collections.Generic.List<HidDeviceInfo> list = HidEnum.Find(VendorId, ProductId);
                for (int i = 0; i < list.Count; i++)
                    if (list[i].UsagePage == UsagePage) { found = list[i]; break; }
                if (found == null)
                {
                    lock (_lock) { _lastError = "未找到耳机灯控接口 (0x3837:0x6008 / 0xFF00)"; }
                    return false;
                }
                HidDevice dev = HidDevice.Open(found);
                lock (_lock)
                {
                    _hid = dev;
                    _open = true;
                    _lastError = "";
                    _hasLast = false;
                }
                _readerRun = true;
                _reader = new Thread(ReaderLoop);
                _reader.IsBackground = true;
                _reader.Name = "headset-acks";
                _reader.Priority = ThreadPriority.BelowNormal;
                _reader.Start();
                Thread.Sleep(30);
                SendCurrent(Rgb.White, true);
                return true;
            }
            catch (Exception ex)
            {
                lock (_lock) { _lastError = "打开耳机失败: " + ex.Message; }
                return false;
            }
        }

        private void ReaderLoop()
        {
            byte[] buf = new byte[128];
            while (_readerRun)
            {
                HidDevice dev;
                lock (_lock) { dev = _hid; }
                if (dev == null) break;
                try
                {
                    int n = dev.Read(buf, 120);
                    if (n > 0)
                    {
                        _ackCount++;
                        // 0x07 status replies start 00 AA 07 ... with RGB at 13..15
                        if (n >= 16 && buf[1] == ReportId && buf[2] == 0x07)
                        {
                            _probeR = buf[13];
                            _probeG = buf[14];
                            _probeB = buf[15];
                            _hasProbe = true;
                        }
                    }
                }
                catch { Thread.Sleep(50); }
            }
        }

        public void Close()
        {
            _readerRun = false;
            Thread reader;
            HidDevice dev;
            lock (_lock)
            {
                reader = _reader;
                _reader = null;
                dev = _hid;
                _hid = null;
                _open = false;
            }
            if (reader != null) { try { reader.Join(400); } catch { } }
            if (dev != null) { try { dev.Dispose(); } catch { } }
        }

        private void SendCurrent(Rgb color, bool force)
        {
            double now = Now();
            if (!force && now - _lastSent < MinInterval) return;
            if (!force && _hasLast && Rgb.Same(color, _lastRgb)) return;

            HidDevice dev;
            int mode;
            byte brightness, direction;
            lock (_lock)
            {
                dev = _hid;
                mode = _mode;
                brightness = _brightness;
                direction = _direction;
            }
            if (dev == null) return;

            byte[] wire = BuildWire((byte)mode, color, brightness, direction);
            byte[] buf = new byte[65];
            Array.Copy(wire, 0, buf, 1, 64);
            _lastSent = now;
            _lastRgb = color;
            _hasLast = true;

            bool ok = false;
            // Measured with the device's own 0x07 readback: only the interrupt
            // OUT path (WriteFile with the 65-byte buffer) actually applies a
            // colour. HidD_SetOutputReport returns True and gets an ACK but the
            // firmware ignores it, which is exactly why the headset never lit up.
            try { ok = dev.Write(buf, 65); } catch { }
            if (ok) Interlocked.Increment(ref _applied);
            else
            {
                Interlocked.Increment(ref _failed);
                _hasLast = false;
                if (Interlocked.Read(ref _failed) > 400)
                {
                    lock (_lock) { _lastError = "耳机写入持续失败，请重新扫描设备"; }
                }
            }
        }

        private static double Now()
        {
            return Environment.TickCount / 1000.0;
        }

        private static byte[] BuildWire(byte mode, Rgb color, byte brightness, byte direction)
        {
            byte[] p = new byte[64];
            p[0] = ReportId;
            p[1] = 0x27;
            p[5] = 0x0A;
            p[6] = 0x01;
            p[7] = mode;
            p[8] = 0x05;
            p[9] = direction;
            p[10] = brightness;
            p[12] = color.R;
            p[13] = color.G;
            p[14] = color.B;
            return p;
        }

        public void Apply(Frame frame)
        {
            if (!IsOpen) return;
            int mode;
            lock (_lock) { mode = _mode; }
            // In a firmware animation mode the headset animates on its own; the
            // app only sets the base colour occasionally.
            SendCurrent(frame.Headset, mode != 0);
        }

        public void NoteError(string message)
        {
            lock (_lock) { _lastError = message; }
        }

        /// <summary>
        /// Query the device (0x07) and decode the current RGB. Reply layout:
        /// 00 AA 07 02 00 00 1E 01 00 06 00 03 00 &lt;R G B&gt; ... "V9 Turbo" ...
        /// The reader thread parses replies continuously, so just ask and wait.
        /// </summary>
        public string Probe()
        {
            HidDevice dev;
            lock (_lock) { dev = _hid; }
            if (dev == null) return "not open";
            try
            {
                _hasProbe = false;
                byte[] wire = new byte[64];
                wire[0] = ReportId;
                wire[1] = 0x07;
                byte[] buf = new byte[65];
                Array.Copy(wire, 0, buf, 1, 64);
                dev.Write(buf, 65);
                for (int i = 0; i < 30; i++)
                {
                    if (_hasProbe) return "rgb=" + _probeR + "," + _probeG + "," + _probeB;
                    Thread.Sleep(20);
                }
                return "no 0x07 reply";
            }
            catch (Exception ex) { return "probe error: " + ex.Message; }
        }
    }
}
