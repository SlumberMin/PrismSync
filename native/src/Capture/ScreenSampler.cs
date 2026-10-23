// PrismSync native app - screen colour sampler (32 x 18 down-sample of the primary display).
//
// GDI objects are created once and reused. A small background worker owns them: it
// reads the primary display every RefreshIntervalMs (default 200 ms) and publishes
// the 32x18 grid. Grab() only reports the published frame (microseconds), so the
// render loop never pays for the screen read; DataAgeMs tells how stale it is.
// Call Refresh() when a guaranteed-fresh frame is needed (tests).
//
// Down-sample pipeline (two stages, one screen read):
//   1. COLORONCOLOR put the 1920x1080 screen into a 320x180 bitmap (point sample),
//   2. HALFTONE average that into the final 32x18 DDB, then GetDIBits into a pinned buffer.
// A direct HALFTONE pass from 1920x1080 gives the same colours but costs ~27% more
// on this machine; have a look at the "colour check" line printed by capture_test.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PrismSync
{
    /// <summary>Downsampled screen capture, used by the ambient ("屏幕氛围") effect.</summary>
    public sealed class ScreenSampler : IDisposable
    {
        public const int Cols = 32;
        public const int Rows = 18;
        private const int CellCount = Cols * Rows;

        private const int MidCols = 320;      // intermediate resolution (10x10 source points per cell)
        private const int MidRows = 180;

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int COLORONCOLOR = 3;
        private const int HALFTONE = 4;
        private const uint SRCCOPY = 0x00CC0020;
        private const uint DIB_RGB_COLORS = 0;
        private const int DefaultRefreshMs = 200;

        // ------------------------------------------------------------- public state
        public bool Available { get; private set; }
        public string LastError { get; private set; }
        /// <summary>Width of the primary display that is being sampled.</summary>
        public int SourceWidth { get; private set; }
        public int SourceHeight { get; private set; }
        /// <summary>Number of successful Grab() calls (cached or not).</summary>
        public long GrabCount { get { return Interlocked.Read(ref _grabCount); } }
        /// <summary>Number of real screen reads performed by the worker / Refresh().</summary>
        public long CaptureCount { get { return Interlocked.Read(ref _captureCount); } }
        /// <summary>Cost of the last Grab() call, in milliseconds (target &lt; 2 ms).</summary>
        public double LastGrabMs { get { return _lastGrabMs; } }
        /// <summary>Cost of the last real screen read (this machine: ~12 ms, environment bound).</summary>
        public double LastCaptureMs { get { lock (_sync) { return _lastCaptureMs; } } }
        /// <summary>Mean cost of a real screen read since construction.</summary>
        public double AverageCaptureMs { get { lock (_sync) { return _captureCount > 0 ? _totalCaptureMs / _captureCount : 0.0; } } }
        /// <summary>Age of the published frame, in milliseconds, or -1 before the first frame.</summary>
        public double DataAgeMs
        {
            get
            {
                lock (_sync)
                {
                    if (!_hasFrame) return -1.0;
                    return (_clock.Elapsed.TotalSeconds - _lastCaptureAt) * 1000.0;
                }
            }
        }
        /// <summary>True once at least one frame was captured.</summary>
        public bool HasData { get { lock (_sync) { return _hasFrame; } } }
        /// <summary>How often the worker refreshes while Grab() is being called (50..2000 ms).</summary>
        public int RefreshIntervalMs
        {
            get { return _refreshIntervalMs; }
            set
            {
                int v = value;
                if (v < 50) v = 50;
                else if (v > 2000) v = 2000;
                _refreshIntervalMs = v;
                try { _wake.Set(); } catch { }
            }
        }

        // -------------------------------------------------------------- GDI state
        private readonly uint[] _pixels = new uint[CellCount];        // published frame
        private readonly uint[] _capturePixels = new uint[CellCount]; // worker staging buffer
        private GCHandle _pixelsPin;
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _bitmap = IntPtr.Zero;
        private IntPtr _dibBits = IntPtr.Zero;
        private readonly byte[] _dibScratch = new byte[CellCount * 4];
        private IntPtr _midDc = IntPtr.Zero;
        private IntPtr _midBitmap = IntPtr.Zero;
        private BitmapInfo _bmi;
        private bool _ready;
        private bool _disposed;

        private readonly object _sync = new object();
        private readonly object _gdiLock = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private Thread _worker;
        private volatile bool _workerRun;
        private volatile int _refreshIntervalMs = DefaultRefreshMs;
        private double _lastGrabAt = -100.0;   // written on the caller thread only

        private bool _hasFrame;
        private Rgb _average;
        private double _lastCaptureAt = -100.0;
        private double _lastCaptureMs;
        private double _totalCaptureMs;
        private double _lastGrabMs;
        private long _captureCount;
        private long _grabCount;

        public ScreenSampler()
        {
            _pixelsPin = GCHandle.Alloc(_capturePixels, GCHandleType.Pinned);
            try
            {
                if (Initialize()) CaptureAndPublish();      // one synchronous first frame
            }
            catch (Exception ex)
            {
                SetFailure("屏幕采样初始化异常: " + ex.Message);
            }

            // The worker retries Initialize() if the desktop was not available yet.
            _workerRun = true;
            Thread t = new Thread(WorkerLoop);
            t.IsBackground = true;
            t.Name = "screen-sampler";
            t.Priority = ThreadPriority.BelowNormal;
            _worker = t;
            try
            {
                t.Start();
            }
            catch (Exception ex)
            {
                _workerRun = false;
                _worker = null;
                SetFailure("屏幕采样线程启动失败: " + ex.Message + "（仍可手动 Refresh()）");
            }
        }

        // ------------------------------------------------------------------- read
        /// <summary>
        /// Report the published frame. Never reads the screen and never blocks, so it
        /// stays far below 2 ms; the worker refreshes in the background. The bool is
        /// false only before the very first frame was captured.
        /// </summary>
        public bool Grab()
        {
            if (_disposed) return false;
            long t0 = Stopwatch.GetTimestamp();
            _lastGrabAt = _clock.Elapsed.TotalSeconds;

            bool have;
            double age;
            lock (_sync)
            {
                have = _hasFrame;
                age = _lastGrabAt - _lastCaptureAt;
            }
            if (have && age >= _refreshIntervalMs * 0.001)
            {
                try { _wake.Set(); } catch { }
            }

            Interlocked.Increment(ref _grabCount);
            _lastGrabMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            return have;
        }

        /// <summary>Force a synchronous screen read and publish it (used by tests/debug).</summary>
        public bool Refresh()
        {
            if (_disposed) return false;
            return CaptureAndPublish();
        }

        /// <summary>Colour of one cell of the 32x18 grid (0 = left/top).</summary>
        public Rgb Cell(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Cols || y >= Rows) return Rgb.Black;
            uint p;
            lock (_sync) { p = _pixels[y * Cols + x]; }     // BGRA in memory
            return new Rgb((byte)((p >> 16) & 0xFF), (byte)((p >> 8) & 0xFF), (byte)(p & 0xFF));
        }

        /// <summary>Average colour of the whole screen (of the published frame).</summary>
        public Rgb Average()
        {
            lock (_sync) { return _average; }
        }

        // ----------------------------------------------------------------- worker
        private void WorkerLoop()
        {
            while (_workerRun)
            {
                try
                {
                    bool asked = _wake.WaitOne(_refreshIntervalMs);
                    if (!_workerRun) break;
                    if (_disposed) break;

                    double now = _clock.Elapsed.TotalSeconds;
                    // Nobody is rendering the screen effect: stay idle (no screen reads).
                    if (now - _lastGrabAt > 1.0) continue;

                    double age;
                    lock (_sync) { age = now - _lastCaptureAt; }
                    if (!asked && age < _refreshIntervalMs * 0.001) continue;

                    CaptureAndPublish();
                }
                catch (Exception ex)
                {
                    SetFailure("屏幕采样异常: " + ex.Message);
                }
            }
        }

        private bool CaptureAndPublish()
        {
            if (_disposed) return false;
            if (!_ready && !Initialize()) return false;

            long t0 = Stopwatch.GetTimestamp();
            bool ok = CaptureInto(_capturePixels);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            if (!ok) return false;

            double now = _clock.Elapsed.TotalSeconds;
            lock (_sync)
            {
                Array.Copy(_capturePixels, _pixels, CellCount);
                _average = ComputeAverage(_pixels);
                _lastCaptureAt = now;
                _lastCaptureMs = ms;
                _totalCaptureMs += ms;
                _captureCount++;
                _hasFrame = true;
                Available = true;
                LastError = null;
            }
            return true;
        }

        private bool CaptureInto(uint[] destination)
        {
            lock (_gdiLock)
            {
                IntPtr screen = Native.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero)
                {
                    SetFailure("GetDC(0) 失败，无法访问桌面");
                    return false;
                }
                try
                {
                    // The intermediate bitmap must stay selected for the whole two
                    // stage pass: deselecting it (as the first version did) leaves a
                    // 1x1 monochrome default bitmap in the DC and stage 2 copies
                    // pure black from it.
                    IntPtr oldMid = Native.SelectObject(_midDc, _midBitmap);
                    IntPtr old = Native.SelectObject(_memDc, _bitmap);
                    if (oldMid == IntPtr.Zero || old == IntPtr.Zero)
                    {
                        SetFailure("SelectObject(位图) 失败");
                        return false;
                    }
                    try
                    {
                        // Stage 1: cheap point-sample shrink to the intermediate bitmap.
                        Native.SetStretchBltMode(_midDc, COLORONCOLOR);
                        if (!Native.StretchBlt(_midDc, 0, 0, MidCols, MidRows,
                                               screen, 0, 0, SourceWidth, SourceHeight, SRCCOPY))
                        {
                            SetFailure("StretchBlt 阶段1 失败 (Win32 " + Marshal.GetLastWin32Error() + ")");
                            return false;
                        }

                        // Stage 2: HALFTONE average into the final 32x18 DIB.
                        Native.SetStretchBltMode(_memDc, HALFTONE);
                        Native.SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
                        if (!Native.StretchBlt(_memDc, 0, 0, Cols, Rows,
                                               _midDc, 0, 0, MidCols, MidRows, SRCCOPY))
                        {
                            SetFailure("StretchBlt 阶段2 失败 (Win32 " + Marshal.GetLastWin32Error() + ")");
                            return false;
                        }
                    }
                    finally
                    {
                        Native.SelectObject(_memDc, old);
                        Native.SelectObject(_midDc, oldMid);
                    }

                    // The DIB section's bits are the pixels: BGRA, top-down.
                    if (_dibBits == IntPtr.Zero)
                    {
                        SetFailure("DIB 像素指针为空");
                        return false;
                    }
                    Marshal.Copy(_dibBits, _dibScratch, 0, CellCount * 4);
                    for (int i = 0; i < CellCount; i++)
                    {
                        int o = i * 4;
                        destination[i] = (uint)(_dibScratch[o] |
                                                (_dibScratch[o + 1] << 8) |
                                                (_dibScratch[o + 2] << 16) |
                                                (_dibScratch[o + 3] << 24));
                    }
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ setup
        private bool Initialize()
        {
            if (_disposed)
            {
                SetFailure("屏幕采样已释放");
                return false;
            }
            lock (_gdiLock)
            {
                if (_ready) return true;

                IntPtr screen = Native.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero)
                {
                    SetFailure("GetDC(0) 失败，无法访问桌面");
                    return false;
                }

                IntPtr dc = IntPtr.Zero;
                IntPtr bmp = IntPtr.Zero;
                IntPtr midDc = IntPtr.Zero;
                IntPtr midBmp = IntPtr.Zero;
                try
                {
                    int w = Native.GetSystemMetrics(SM_CXSCREEN);
                    int h = Native.GetSystemMetrics(SM_CYSCREEN);
                    if (w <= 0 || h <= 0)
                    {
                        SetFailure("主屏尺寸无效 (" + w + "x" + h + ")");
                        return false;
                    }

                    BitmapInfo bmi = new BitmapInfo();
                    bmi.Header.biSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
                    bmi.Header.biWidth = Cols;
                    bmi.Header.biHeight = -Rows;             // negative = top-down DIB
                    bmi.Header.biPlanes = 1;
                    bmi.Header.biBitCount = 32;
                    bmi.Header.biCompression = 0;            // BI_RGB
                    bmi.Header.biSizeImage = (uint)(CellCount * 4);

                    dc = Native.CreateCompatibleDC(screen);
                    midDc = Native.CreateCompatibleDC(screen);
                    midBmp = Native.CreateCompatibleBitmap(screen, MidCols, MidRows);
                    // A DIB section gives us the pixels directly: GetDIBits on a
                    // compatible bitmap kept returning an all-zero buffer here.
                    IntPtr bits;
                    bmp = Native.CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
                    if (dc == IntPtr.Zero || bmp == IntPtr.Zero || midDc == IntPtr.Zero || midBmp == IntPtr.Zero)
                    {
                        SetFailure("创建 GDI 位图/DC 失败 (Win32 " + Marshal.GetLastWin32Error() + ")");
                        return false;
                    }

                    _bmi = bmi;
                    _dibBits = bits;
                    _memDc = dc;
                    _bitmap = bmp;
                    _midDc = midDc;
                    _midBitmap = midBmp;
                    dc = IntPtr.Zero;
                    bmp = IntPtr.Zero;
                    midDc = IntPtr.Zero;
                    midBmp = IntPtr.Zero;

                    SourceWidth = w;
                    SourceHeight = h;
                    _ready = true;
                    return true;
                }
                finally
                {
                    if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
                    if (dc != IntPtr.Zero) Native.DeleteDC(dc);
                    if (midBmp != IntPtr.Zero) Native.DeleteObject(midBmp);
                    if (midDc != IntPtr.Zero) Native.DeleteDC(midDc);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        private static Rgb ComputeAverage(uint[] pixels)
        {
            int r = 0, g = 0, b = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                uint p = pixels[i];
                r += (int)((p >> 16) & 0xFF);
                g += (int)((p >> 8) & 0xFF);
                b += (int)(p & 0xFF);
            }
            int n = pixels.Length;
            return new Rgb((byte)((r + n / 2) / n), (byte)((g + n / 2) / n), (byte)((b + n / 2) / n));
        }

        private void SetFailure(string message)
        {
            lock (_sync)
            {
                Available = false;
                LastError = message;
            }
        }

        // -------------------------------------------------------------- lifecycle
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _workerRun = false;
            try { _wake.Set(); } catch { }

            Thread w = _worker;
            _worker = null;
            if (w != null && w != Thread.CurrentThread)
            {
                try { w.Join(2000); } catch { }
            }

            lock (_gdiLock)
            {
                ReleaseGdi();
            }
            if (_pixelsPin.IsAllocated) _pixelsPin.Free();
            try { _wake.Close(); } catch { }
            Available = false;
        }

        private void ReleaseGdi()
        {
            if (_midBitmap != IntPtr.Zero) { Native.DeleteObject(_midBitmap); _midBitmap = IntPtr.Zero; }
            if (_midDc != IntPtr.Zero) { Native.DeleteDC(_midDc); _midDc = IntPtr.Zero; }
            if (_bitmap != IntPtr.Zero) { Native.DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { Native.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            _ready = false;
        }

        // ------------------------------------------------------------------ Win32
        private static class Native
        {
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr GetDC(IntPtr hWnd);

            [DllImport("user32.dll")]
            internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

            [DllImport("user32.dll")]
            internal static extern int GetSystemMetrics(int nIndex);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi,
                uint usage, out IntPtr bits, IntPtr section, uint offset);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern bool DeleteObject(IntPtr hObject);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            internal static extern int SetStretchBltMode(IntPtr hdc, int mode);

            [DllImport("gdi32.dll")]
            internal static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                                                   IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

            [DllImport("gdi32.dll", SetLastError = true)]
            internal static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines,
                                                 IntPtr lpvBits, ref BitmapInfo lpbmi, uint usage);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colors;                             // one RGBQUAD placeholder
        }
    }
}
