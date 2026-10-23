// PrismSync native app - microphone level meter (winmm waveIn, 16 kHz / 16-bit / mono).
//
// A dedicated thread owns the recording device and keeps four small event-callback
// buffers in flight, so a slow rendered frame never drops audio. Every finished
// buffer is analysed on that thread (RMS level, three Goertzel bands, beat
// transient) and re-queued immediately. No WAV files, no recorded audio is kept.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PrismSync
{
    /// <summary>Microphone level + coarse bands, used by the audio-reactive effect.</summary>
    public sealed class AudioMeter : IDisposable
    {
        public const int SampleRate = 16000;
        public const int BufferMilliseconds = 32;
        private const int BufferSamples = SampleRate * BufferMilliseconds / 1000;   // 512
        private const int BufferCount = 4;
        private const int BufferBytes = BufferSamples * 2;

        // ------------------------------------------------------------- public state
        public bool Available { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Smoothed RMS level 0..1.</summary>
        public float Level { get; private set; }
        public float Bass { get; private set; }
        public float Mid { get; private set; }
        public float Treble { get; private set; }
        /// <summary>True on the frame a transient was detected (held ~90 ms).</summary>
        public bool Beat { get; private set; }
        /// <summary>Smoothed short-term peak 0..1 (useful for meters).</summary>
        public float Peak { get; private set; }
        /// <summary>Number of samples recorded since the last Start().</summary>
        public long SampleCount { get { return _sampleCount; } }
        public int BeatCount { get { return _beatCount; } }
        /// <summary>Friendly name of the opened recording device, or null.</summary>
        public string DeviceName { get; private set; }
        /// <summary>waveIn device index, -1 when unknown.</summary>
        public int DeviceId { get; private set; }
        public bool IsRunning { get { return _running; } }

        // ----------------------------------------------------------------- constants
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_EVENT = 0x00050000;
        private const uint WAVE_FORMAT_PCM = 1;
        private const uint WHDR_DONE = 0x00000001;
        private const uint WAIT_OBJECT_0 = 0;

        // Goertzel bins for a 512-sample frame at 16 kHz (bin = 31.25 Hz).
        private static readonly int[] BassBins = new int[] { 2, 3, 4, 5 };            //  62..156 Hz
        private static readonly int[] MidBins = new int[] { 14, 20, 28, 36, 44 };     // 437..1375 Hz
        private static readonly int[] TrebleBins = new int[] { 80, 100, 128, 160, 200 }; // 2.5..6.25 kHz

        // ------------------------------------------------------------------- state
        private readonly object _startLock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly float[] _bassHistory = new float[43];   // ~1.4 s of 32 ms frames

        private Thread _thread;
        private volatile bool _running;
        private bool _disposed;

        private IntPtr _hwi = IntPtr.Zero;
        private IntPtr _event = IntPtr.Zero;
        private IntPtr _headers = IntPtr.Zero;
        private IntPtr[] _buffers;
        private int _headerSize;

        private short[] _scratch = new short[BufferSamples];
        private long _sampleCount;
        private int _beatCount;
        private double _openedAt;
        private bool _noDataReported;

        private int _historyPos;
        private int _historyCount;
        private double _historySum;
        private double _lastBeatAt;
        private double _beatClearAt;

        public AudioMeter()
        {
            DeviceId = -1;
            _lastBeatAt = -10.0;
        }

        // ----------------------------------------------------------------- control
        /// <summary>
        /// Open the default microphone and start measuring. Safe to call again after
        /// Stop(); never blocks longer than a driver open and never throws.
        /// </summary>
        public bool Start()
        {
            lock (_startLock)
            {
                if (_disposed)
                {
                    Available = false;
                    LastError = "音频采集已释放";
                    return false;
                }
                if (_running) return Available;

                ResetState();
                if (!OpenDevice())
                {
                    CloseDevice();
                    Available = false;
                    return false;
                }

                _running = true;
                _openedAt = _clock.Elapsed.TotalSeconds;
                Thread t = new Thread(ProcessLoop);
                t.IsBackground = true;
                t.Name = "mic-meter";
                t.Priority = ThreadPriority.AboveNormal;
                _thread = t;
                try
                {
                    t.Start();
                }
                catch (Exception ex)
                {
                    _running = false;
                    _thread = null;
                    CloseDevice();
                    Available = false;
                    LastError = "麦克风线程启动失败: " + ex.Message;
                    return false;
                }

                Available = true;
                LastError = null;
                return true;
            }
        }

        public void Stop()
        {
            lock (_startLock)
            {
                if (!_running && _hwi == IntPtr.Zero)
                {
                    Available = false;
                    return;
                }

                _running = false;
                Thread t = _thread;
                _thread = null;
                if (t != null && t != Thread.CurrentThread)
                {
                    bool joined;
                    try { joined = t.Join(2000); }
                    catch { joined = true; }
                    if (!joined)
                    {
                        // The worker never sleeps for long; this is only a pathological driver stall.
                        Available = false;
                        LastError = "麦克风线程未能在 2 秒内退出（驱动无响应），已保留设备句柄以免竞态";
                        return;
                    }
                }

                CloseDevice();
                Available = false;
                Level = 0f;
                Bass = 0f;
                Mid = 0f;
                Treble = 0f;
                Peak = 0f;
                Beat = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch { }
        }

        // ------------------------------------------------------------------ open
        private bool OpenDevice()
        {
            uint count = Native.waveInGetNumDevs();
            if (count == 0)
            {
                Fail("未检测到麦克风设备（waveInGetNumDevs = 0）");
                return false;
            }

            _event = Native.CreateEvent(IntPtr.Zero, false, false, null);
            if (_event == IntPtr.Zero)
            {
                Fail("创建音频事件失败 (Win32 " + Marshal.GetLastWin32Error() + ")");
                return false;
            }

            WaveFormatEx fmt = new WaveFormatEx();
            fmt.wFormatTag = (ushort)WAVE_FORMAT_PCM;
            fmt.nChannels = 1;
            fmt.nSamplesPerSec = SampleRate;
            fmt.nAvgBytesPerSec = SampleRate * 2;
            fmt.nBlockAlign = 2;
            fmt.wBitsPerSample = 16;
            fmt.cbSize = 0;

            IntPtr hwi = IntPtr.Zero;
            int device = -1;
            uint rc = Native.waveInOpen(out hwi, WAVE_MAPPER, ref fmt, _event, IntPtr.Zero, CALLBACK_EVENT);
            if (rc != 0)
            {
                // Default device refused (privacy / busy / disabled): try each device explicitly.
                hwi = IntPtr.Zero;
                for (uint i = 0; i < count; i++)
                {
                    rc = Native.waveInOpen(out hwi, i, ref fmt, _event, IntPtr.Zero, CALLBACK_EVENT);
                    if (rc == 0)
                    {
                        device = (int)i;
                        break;
                    }
                }
            }
            if (rc != 0)
            {
                Fail("打开麦克风失败: " + ErrorText(rc));
                return false;
            }
            _hwi = hwi;

            uint actualId = 0;
            if (Native.waveInGetID(_hwi, out actualId) == 0) device = (int)actualId;
            DeviceId = device;
            DeviceName = QueryDeviceName(device);

            if (!QueueBuffers())
            {
                Fail("麦克风缓冲准备失败（waveInPrepareHeader / waveInAddBuffer）");
                return false;
            }

            rc = Native.waveInStart(_hwi);
            if (rc != 0)
            {
                Fail("启动麦克风录音失败: " + ErrorText(rc));
                return false;
            }
            return true;
        }

        private bool QueueBuffers()
        {
            _headerSize = Marshal.SizeOf(typeof(WaveHeader));
            _headers = Marshal.AllocHGlobal(_headerSize * BufferCount);
            if (_headers == IntPtr.Zero)
            {
                _headerSize = 0;
                return false;
            }
            _buffers = new IntPtr[BufferCount];
            for (int i = 0; i < BufferCount; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                if (_buffers[i] == IntPtr.Zero) return false;

                WaveHeader h = new WaveHeader();
                h.lpData = _buffers[i];
                h.dwBufferLength = (uint)BufferBytes;
                Marshal.StructureToPtr(h, HeaderPtr(i), false);

                if (Native.waveInPrepareHeader(_hwi, HeaderPtr(i), (uint)_headerSize) != 0) return false;
                if (Native.waveInAddBuffer(_hwi, HeaderPtr(i), (uint)_headerSize) != 0) return false;
            }
            return true;
        }

        private void CloseDevice()
        {
            if (_hwi != IntPtr.Zero)
            {
                Native.waveInStop(_hwi);
                Native.waveInReset(_hwi);
                if (_headers != IntPtr.Zero && _headerSize > 0)
                {
                    for (int i = 0; i < BufferCount; i++)
                        Native.waveInUnprepareHeader(_hwi, HeaderPtr(i), (uint)_headerSize);
                }
                Native.waveInClose(_hwi);
                _hwi = IntPtr.Zero;
            }
            if (_headers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headers);
                _headers = IntPtr.Zero;
            }
            if (_buffers != null)
            {
                for (int i = 0; i < _buffers.Length; i++)
                    if (_buffers[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
                _buffers = null;
            }
            if (_event != IntPtr.Zero)
            {
                Native.CloseHandle(_event);
                _event = IntPtr.Zero;
            }
            _headerSize = 0;
        }

        private IntPtr HeaderPtr(int index)
        {
            return new IntPtr(_headers.ToInt64() + (long)index * _headerSize);
        }

        // ----------------------------------------------------------------- worker
        private void ProcessLoop()
        {
            try
            {
                while (_running)
                {
                    uint wait = Native.WaitForSingleObject(_event, 20);
                    if (!_running) break;
                    if (wait == WAIT_OBJECT_0) ProcessDoneBuffers();

                    double now = _clock.Elapsed.TotalSeconds;
                    if (Beat && now >= _beatClearAt) Beat = false;
                    CheckNoData(now);
                }
            }
            catch (Exception ex)
            {
                Available = false;
                LastError = "麦克风采集异常: " + ex.Message;
            }
        }

        private void ProcessDoneBuffers()
        {
            for (int i = 0; i < BufferCount; i++)
            {
                IntPtr ptr = HeaderPtr(i);
                WaveHeader h = (WaveHeader)Marshal.PtrToStructure(ptr, typeof(WaveHeader));
                if ((h.dwFlags & WHDR_DONE) == 0) continue;
                if (!_running) return;

                if (h.dwBytesRecorded > 0 && h.dwBytesRecorded <= (uint)BufferBytes)
                    OnSamples(h.lpData, (int)h.dwBytesRecorded);

                Native.waveInUnprepareHeader(_hwi, ptr, (uint)_headerSize);
                WaveHeader again = new WaveHeader();
                again.lpData = h.lpData;
                again.dwBufferLength = (uint)BufferBytes;
                Marshal.StructureToPtr(again, ptr, false);
                if (Native.waveInPrepareHeader(_hwi, ptr, (uint)_headerSize) == 0)
                    Native.waveInAddBuffer(_hwi, ptr, (uint)_headerSize);
            }
        }

        private void CheckNoData(double now)
        {
            if (_sampleCount > 0 || _noDataReported) return;
            if (now - _openedAt < 2.5) return;
            _noDataReported = true;
            Available = false;
            LastError = "麦克风已打开但 2.5 秒内没有音频数据（可能被 Windows 隐私设置拒绝：设置 > 隐私和安全性 > 麦克风）";
        }

        // --------------------------------------------------------------- analysis
        private void OnSamples(IntPtr data, int bytes)
        {
            int n = bytes / 2;
            if (n <= 0) return;
            if (_scratch.Length < n) _scratch = new short[n];
            Marshal.Copy(data, _scratch, 0, n);
            _sampleCount += n;

            double sumSq = 0.0;
            int peak = 0;
            for (int i = 0; i < n; i++)
            {
                int v = _scratch[i];
                sumSq += (double)v * v;
                int a = v < 0 ? -v : v;
                if (a > peak) peak = a;
            }

            double rms = Math.Sqrt(sumSq / n) / 32768.0;
            float target = (float)Math.Min(1.0, Math.Sqrt(rms * 4.0));
            Level = Approach(Level, target, 0.55f, 0.12f);
            Peak = Math.Max(Peak * 0.85f, (float)(peak / 32768.0));

            float b = (float)Band(BandEnergy(_scratch, n, BassBins), 3.0);
            float m = (float)Band(BandEnergy(_scratch, n, MidBins), 4.0);
            float tr = (float)Band(BandEnergy(_scratch, n, TrebleBins), 6.0);
            Bass = Approach(Bass, b, 0.60f, 0.18f);
            Mid = Approach(Mid, m, 0.60f, 0.18f);
            Treble = Approach(Treble, tr, 0.60f, 0.18f);

            double now = _clock.Elapsed.TotalSeconds;

            // Beat: instantaneous bass well above its own ~1.4 s rolling average.
            double avg = _historyCount > 0 ? _historySum / _historyCount : 0.0;
            if (b > 0.10f && b > avg * 1.35 && now - _lastBeatAt >= 0.25)
            {
                Beat = true;
                _beatCount++;
                _lastBeatAt = now;
                _beatClearAt = now + 0.09;
            }

            _historySum -= _bassHistory[_historyPos];
            _bassHistory[_historyPos] = b;
            _historySum += b;
            _historyPos = (_historyPos + 1) % _bassHistory.Length;
            if (_historyCount < _bassHistory.Length) _historyCount++;

            _noDataReported = false;
            if (!Available || LastError != null)
            {
                Available = true;
                LastError = null;
            }
        }

        private static double BandEnergy(short[] data, int n, int[] bins)
        {
            if (n < 128) return 0.0;
            double sum = 0.0;
            int used = 0;
            for (int bi = 0; bi < bins.Length; bi++)
            {
                int k = (int)Math.Round(bins[bi] * (double)n / BufferSamples);
                if (k < 1 || k >= n / 2) continue;
                sum += Goertzel(data, n, k);
                used++;
            }
            return used > 0 ? sum / used : 0.0;
        }

        /// <summary>Amplitude (≈ peak value in sample units) at bin k of n samples.</summary>
        private static double Goertzel(short[] data, int n, int k)
        {
            double w = 2.0 * Math.PI * k / n;
            double coeff = 2.0 * Math.Cos(w);
            double s0 = 0.0, s1 = 0.0, s2 = 0.0;
            for (int i = 0; i < n; i++)
            {
                s0 = data[i] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
            if (power < 0.0) power = 0.0;
            return Math.Sqrt(power) * 2.0 / n;
        }

        private static double Band(double amplitude, double gain)
        {
            double norm = amplitude / 32768.0;
            if (norm <= 0.0) return 0.0;
            double v = Math.Sqrt(norm * gain);
            return v > 1.0 ? 1.0 : v;
        }

        private static float Approach(float current, float target, float attack, float release)
        {
            if (current < 0f) current = 0f;
            else if (current > 1f) current = 1f;
            if (target < 0f) target = 0f;
            else if (target > 1f) target = 1f;
            float coef = target > current ? attack : release;
            return current + (target - current) * coef;
        }

        // ----------------------------------------------------------------- helpers
        private void ResetState()
        {
            Level = 0f;
            Bass = 0f;
            Mid = 0f;
            Treble = 0f;
            Peak = 0f;
            Beat = false;
            _sampleCount = 0;
            _beatCount = 0;
            _noDataReported = false;
            _historyPos = 0;
            _historyCount = 0;
            _historySum = 0.0;
            _lastBeatAt = -10.0;
            _beatClearAt = 0.0;
            for (int i = 0; i < _bassHistory.Length; i++) _bassHistory[i] = 0f;
            DeviceName = null;
            DeviceId = -1;
            LastError = null;
        }

        private void Fail(string message)
        {
            Available = false;
            LastError = message;
        }

        private static string QueryDeviceName(int deviceId)
        {
            try
            {
                WaveInCaps caps = new WaveInCaps();
                uint rc = Native.waveInGetDevCapsW(new UIntPtr(unchecked((uint)deviceId)),
                                                   ref caps, (uint)Marshal.SizeOf(typeof(WaveInCaps)));
                if (rc == 0 && caps.szPname != null && caps.szPname.Length > 0) return caps.szPname;
            }
            catch { }
            return deviceId >= 0 ? ("麦克风 #" + deviceId) : "默认麦克风";
        }

        private static string ErrorText(uint code)
        {
            try
            {
                StringBuilder sb = new StringBuilder(256);
                uint rc = Native.waveInGetErrorTextW(code, sb, (uint)sb.Capacity);
                if (rc == 0 && sb.Length > 0) return sb.ToString() + " (MMSYSERR " + code + ")";
            }
            catch { }
            return "MMSYSERR " + code;
        }

        // ------------------------------------------------------------------- Win32
        private static class Native
        {
            [DllImport("winmm.dll", ExactSpelling = true)]
            internal static extern uint waveInGetNumDevs();

            [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern uint waveInGetDevCapsW(UIntPtr uDeviceID, ref WaveInCaps caps, uint cb);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInOpen(out IntPtr hwi, uint uDeviceID, ref WaveFormatEx pwfx,
                                                   IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInGetID(IntPtr hwi, out uint puDeviceID);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInPrepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInUnprepareHeader(IntPtr hwi, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInAddBuffer(IntPtr hwi, IntPtr pwh, uint cbwh);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInStart(IntPtr hwi);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInStop(IntPtr hwi);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInReset(IntPtr hwi);

            [DllImport("winmm.dll", SetLastError = true)]
            internal static extern uint waveInClose(IntPtr hwi);

            [DllImport("winmm.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern uint waveInGetErrorTextW(uint mmrError, StringBuilder lpText, uint cchText);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset,
                                                      bool bInitialState, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr hObject);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WaveInCaps
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }
    }
}
