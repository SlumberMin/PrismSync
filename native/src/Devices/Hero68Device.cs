// PrismSync native app - AULA HERO 68 HE keyboard (per-key HID reports).
//
// Protocol (verified byte-for-byte against the vendor client and live hardware):
//   packet: [0]=0x09 report id, [1]=cmd, [2]=param, [4]=total packets,
//           [5]=packet index, [6]=payload length, [7..]=payload,
//           [63]=(0xFF - sum([0..62])) & 0xFF
//   setup : 0x01/0x80 [0x10] -> 0x04/0x02 [0x13,0x00] -> 0x04/0x04 [0x64,0x04]
//   paint : 0x08/0x01, payload = [R,G,B,count, keyId...] (max 52 ids)
//   commit: 0x01/0x00
// The firmware parses only the FIRST colour segment of a packet, so each packet
// carries exactly one segment and packets that share a colour are merged.
using System;
using System.Collections.Generic;
using System.Threading;

namespace PrismSync
{
    public sealed class Hero68Device : IDeviceAdapter
    {
        public const int VendorId = 0x372E;
        public const int ProductId = 0x103E;
        public const int UsagePage = 0xFF60;
        public const int MaxKeysPerSegment = 52;

        private readonly KeyLayout _layout;
        private readonly object _lock = new object();
        private HidDevice _hid;
        private bool _open;
        private int _consecutiveFailures;
        private long _applied;
        private long _failed;
        private string _lastError = "";
        private bool _customModeReady;
        private readonly byte[] _buffer = new byte[64];
        private readonly List<int> _groups = new List<int>(256);
        private readonly int[] _groupColor = new int[Frame.KeyCount];
        private readonly List<int>[] _idsByGroup = new List<int>[Frame.KeyCount];
        private int _groupCount;

        /// <summary>Colour quantisation levels (0 = smooth, 1 packet per distinct colour).</summary>
        public int Bands = 16;   // quantise per-frame gradients: fewer reports, less USB pressure
        /// <summary>Hue steps for the stable palette (lower = fewer reports).</summary>
        public int HueSteps = 32;
        public int SatLevels = 2;
        public int ValSteps = 8;
        /// <summary>Use the HSV palette instead of per-channel RGB quantisation.</summary>
        public bool UseHsvQuant = true;
        /// <summary>Use the compact cmd 0x06 stream (11 keys per packet, 7 packets/frame).</summary>
        public bool UseCmd06 = false;   // cmd06 flashes on this firmware; keep it only as a lab option
        /// <summary>Fallback path (cmd 0x08): cap packets per frame and quantise adaptively.</summary>
        public int MaxPacketsPerFrame = 24;
        public bool AdaptivePalette = true;
        /// <summary>Fallback path: send only changed keys (off by default, it made more
        /// modes flicker on this firmware).</summary>
        public bool DirtyUpdates = false;
        public int LastPackets;
        public double LastApplyMs;

        private const int FullRefreshFrames = 90;   // ~3 s at 30 fps
        private readonly int[] _quant = new int[Frame.KeyCount];
        private readonly int[] _lastColor = new int[Frame.KeyCount];
        private readonly int[] _colorSeen = new int[Frame.KeyCount];
        private readonly int[] _ledToIndex = new int[128];
        private int _framesSinceFull = FullRefreshFrames;

        public Hero68Device(KeyLayout layout)
        {
            _layout = layout;
            for (int i = 0; i < _idsByGroup.Length; i++) _idsByGroup[i] = new List<int>(8);
            for (int i = 0; i < _lastColor.Length; i++) _lastColor[i] = -1;
            for (int i = 0; i < _ledToIndex.Length; i++) _ledToIndex[i] = -1;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                int id = _layout.LedId[i];
                if (id >= 0 && id < _ledToIndex.Length) _ledToIndex[id] = i;
            }
        }

        public string Id { get { return "keyboard"; } }
        public string Name { get { return "键盘"; } }
        public string Detail { get { return "AULA HERO 68 HE"; } }
        public bool SingleZone { get { return false; } }
        public bool IsOpen { get { lock (_lock) { return _open; } } }
        public string LastError { get { lock (_lock) { return _lastError; } } }
        public long Applied { get { return Interlocked.Read(ref _applied); } }
        public long Failed { get { return Interlocked.Read(ref _failed); } }
        public string Status { get { return ""; } }
        public string Uid = "";

        public bool Open()
        {
            Close();
            try
            {
                HidDeviceInfo found = null;
                List<HidDeviceInfo> list = HidEnum.Find(VendorId, ProductId);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].UsagePage == UsagePage) { found = list[i]; break; }
                }
                if (found == null)
                {
                    SetError("未找到键盘的厂商 HID 接口 (0xFF60)");
                    return false;
                }
                HidDevice dev = HidDevice.Open(found);
                lock (_lock) { _hid = dev; _open = true; _lastError = ""; _consecutiveFailures = 0; }

                Uid = ReadUid();
                if (string.IsNullOrEmpty(Uid))
                {
                    // The interface is there, but the UID handshake failed: keep it
                    // open anyway, colour reports often still work.
                    SetError("键盘已连接，但 UID 读取失败");
                }
                EnterCustomMode();
                return true;
            }
            catch (Exception ex)
            {
                SetError("打开键盘失败: " + ex.Message);
                return false;
            }
        }

        private void SetError(string message)
        {
            lock (_lock) { _lastError = message; }
        }

        public void Close()
        {
            HidDevice dev;
            lock (_lock)
            {
                dev = _hid;
                _hid = null;
                _open = false;
                _customModeReady = false;
            }
            if (dev != null) { try { dev.Dispose(); } catch { } }
        }

        private string ReadUid()
        {
            try
            {
                byte[] buf = new byte[128];
                lock (_lock)
                {
                    if (_hid == null) return "";
                    _hid.Write(Build(0x82, 0x01, null, 1, 0), 64);
                }
                int n = _hid.Read(buf, 1200);
                if (n < 13) return "";
                if (buf[1] != 0x82) return "";
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 7; i < 13; i++) sb.Append(buf[i].ToString("x2"));
                return sb.ToString();
            }
            catch { return ""; }
        }

        private void EnterCustomMode()
        {
            try
            {
                Send(Build(0x01, 0x80, new byte[] { 0x10 }, 1, 0));
                Thread.Sleep(20);
                Send(Build(0x04, 0x02, new byte[] { 0x13, 0x00 }, 1, 0));
                Send(Build(0x04, 0x04, new byte[] { 0x64, 0x04 }, 1, 0));
                lock (_lock) { _customModeReady = true; }
            }
            catch (Exception ex) { SetError("键盘初始化失败: " + ex.Message); }
        }

        private void Send(byte[] packet)
        {
            HidDevice dev;
            lock (_lock) { dev = _hid; }
            if (dev == null) return;
            if (!dev.Write(packet, 64))
            {
                Interlocked.Increment(ref _failed);
                int fails = Interlocked.Increment(ref _consecutiveFailures);
                if (fails >= 60)
                {
                    SetError("键盘写入持续失败，请重新扫描设备");
                    Close();
                }
            }
            else _consecutiveFailures = 0;
        }

        public void Apply(Frame frame)
        {
            HidDevice dev;
            bool ready;
            lock (_lock) { dev = _hid; ready = _customModeReady; }
            if (dev == null || !ready) return;
            long startTicks = DateTime.UtcNow.Ticks;

            if (UseCmd06) ApplyCmd06(dev, frame);
            else if (DirtyUpdates) ApplyDirty(dev, frame);
            else ApplyFull(dev, frame);

            LastApplyMs = (DateTime.UtcNow.Ticks - startTicks) / 10000.0;
        }

        /// <summary>
        /// cmd 0x06 / param 0x00: each packet carries up to 11 independent keys as
        /// [idHi, idLo, R, G, B]. The vendor protocol documents this form and it is
        /// the only way to stay under the firmware's per-frame packet budget: a
        /// 68-key gradient is 7 packets instead of ~68. (cmd 0x08 accepts a whole
        /// colour segment per packet, so gradients explode to one packet per key.)
        /// </summary>
        private void ApplyCmd06(HidDevice dev, Frame frame)
        {
            const int PerPacket = 11;          // 11 * 5 = 55 bytes <= 56 payload
            int packets = (Frame.KeyCount + PerPacket - 1) / PerPacket;
            for (int p = 0; p < packets; p++)
            {
                int start = p * PerPacket;
                int n = Math.Min(PerPacket, Frame.KeyCount - start);
                byte[] payload = new byte[5 * n];
                for (int j = 0; j < n; j++)
                {
                    int i = start + j;
                    int id = _layout.LedId[i];
                    Rgb c = frame.Keyboard[i];
                    int o = j * 5;
                    payload[o] = (byte)((id >> 8) & 0xFF);
                    payload[o + 1] = (byte)(id & 0xFF);
                    payload[o + 2] = c.R;
                    payload[o + 3] = c.G;
                    payload[o + 4] = c.B;
                }
                Send(Build(0x06, 0x00, payload, packets, p));
            }
            Send(Build(0x01, 0x00, null, 1, 0));
            Interlocked.Increment(ref _applied);
            LastPackets = packets;
        }

        private void ApplyFull(HidDevice dev, Frame frame)
        {
            int hs = UseHsvQuant ? HueSteps : 0;
            int sl = SatLevels;
            int vs = ValSteps;
            if (UseHsvQuant && AdaptivePalette)
            {
                // The lab showed 24 colours (= 24 packets) is stable on this firmware
                // while 68 packets loses colours; shrink the palette until it fits.
                while (hs > 3 && CountColors(frame, hs, sl, vs) > MaxPacketsPerFrame) hs -= 2;
                while (vs > 2 && CountColors(frame, hs, sl, vs) > MaxPacketsPerFrame) vs--;
                while (sl > 1 && CountColors(frame, hs, sl, vs) > MaxPacketsPerFrame) sl--;
                if (hs < 3) hs = 3;
            }
            BuildGroups(frame, hs, sl, vs, null);
            int packetCount = PacketCount();
            if (packetCount <= 0) { LastPackets = 0; return; }
            LastPackets = SendGroups(dev, packetCount, packetCount, null);
        }

        private void ApplyDirty(HidDevice dev, Frame frame)
        {
            _framesSinceFull++;
            if (_framesSinceFull >= FullRefreshFrames)
            {
                _framesSinceFull = 1;
                for (int i = 0; i < Frame.KeyCount; i++) _lastColor[i] = -1;
            }
            int hs = UseHsvQuant ? HueSteps : 0;
            int sl = SatLevels;
            int vs = ValSteps;
            if (UseHsvQuant && AdaptivePalette)
            {
                while (hs > 3 && CountColors(frame, hs, sl, vs) > MaxPacketsPerFrame) hs -= 2;
                while (vs > 2 && CountColors(frame, hs, sl, vs) > MaxPacketsPerFrame) vs--;
                if (hs < 3) hs = 3;
            }
            BuildGroups(frame, hs, sl, vs, _lastColor);
            int packetCount = PacketCount();
            if (packetCount <= 0) { LastPackets = 0; return; }
            int cap = Math.Min(packetCount, Math.Max(1, MaxPacketsPerFrame));
            LastPackets = SendGroups(dev, cap, cap, _lastColor);
        }

        private int CountColors(Frame frame, int hueSteps, int satLevels, int valSteps)
        {
            int seen = 0;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                Rgb c = Rgb.QuantizeHsv(frame.Keyboard[i], hueSteps, satLevels, valSteps);
                int key = (c.R << 16) | (c.G << 8) | c.B;
                bool found = false;
                for (int j = 0; j < seen; j++)
                    if (_colorSeen[j] == key) { found = true; break; }
                if (!found && seen < _colorSeen.Length) _colorSeen[seen++] = key;
            }
            return seen;
        }

        private void BuildGroups(Frame frame, int hueSteps, int satLevels, int valSteps, int[] filter)
        {
            for (int g = 0; g < _groupCount; g++) _idsByGroup[g].Clear();
            _groupCount = 0;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                Rgb c = frame.Keyboard[i];
                if (hueSteps > 0)
                {
                    c = Rgb.QuantizeHsv(c, hueSteps, satLevels, valSteps);
                }
                else if (Bands > 0)
                {
                    int step = Math.Max(1, 255 / Bands);
                    c = new Rgb((byte)((c.R / step) * step), (byte)((c.G / step) * step),
                                (byte)((c.B / step) * step));
                }
                int key = (c.R << 16) | (c.G << 8) | c.B;
                _quant[i] = key;
                if (filter != null && filter[i] == key) continue;
                int group = -1;
                for (int g = 0; g < _groupCount; g++)
                    if (_groupColor[g] == key) { group = g; break; }
                if (group < 0)
                {
                    group = _groupCount++;
                    _groupColor[group] = key;
                }
                _idsByGroup[group].Add(_layout.LedId[i]);
            }
        }

        private int PacketCount()
        {
            int packets = 0;
            for (int g = 0; g < _groupCount; g++)
                packets += (_idsByGroup[g].Count + MaxKeysPerSegment - 1) / MaxKeysPerSegment;
            return packets;
        }

        private int SendGroups(HidDevice dev, int total, int cap, int[] mark)
        {
            int index = 0, sent = 0;
            for (int g = 0; g < _groupCount && sent < cap; g++)
            {
                int color = _groupColor[g];
                byte r = (byte)((color >> 16) & 0xFF);
                byte g8 = (byte)((color >> 8) & 0xFF);
                byte b = (byte)(color & 0xFF);
                List<int> ids = _idsByGroup[g];
                for (int start = 0; start < ids.Count && sent < cap; start += MaxKeysPerSegment)
                {
                    int n = Math.Min(MaxKeysPerSegment, ids.Count - start);
                    byte[] payload = new byte[4 + n];
                    payload[0] = r; payload[1] = g8; payload[2] = b; payload[3] = (byte)n;
                    for (int i = 0; i < n; i++)
                    {
                        int led = ids[start + i];
                        payload[4 + i] = (byte)led;
                        if (mark != null)
                        {
                            int keyIndex = _ledToIndex[led];
                            if (keyIndex >= 0) mark[keyIndex] = color;
                        }
                    }
                    Send(Build(0x08, 0x01, payload, total, index++));
                    sent++;
                }
            }
            if (sent > 0)
            {
                Send(Build(0x01, 0x00, null, 1, 0));
                Interlocked.Increment(ref _applied);
            }
            return sent;
        }

        public void NoteError(string message) { SetError(message); }

        private byte[] Build(byte cmd, byte param, byte[] payload, int total, int index)
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
    }
}
