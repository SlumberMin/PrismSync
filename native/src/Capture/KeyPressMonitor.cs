// PrismSync native app - background keyboard monitor built on Raw Input.
//
// The monitor owns a hidden window and its own message loop thread, and registers
// for keyboard Raw Input with RIDEV_INPUTSINK so keystrokes are still reported
// while another application is in the foreground. VK codes are translated to HID
// keyboard usage ids (the same ids UsageLayout uses to place a ripple).
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PrismSync
{
    /// <summary>
    /// Raw-input keyboard monitor (works in the background, not only when the app
    /// is focused). Reports HID keyboard usage ids so effects can place a ripple
    /// at the physical key position.
    /// </summary>
    public sealed class KeyPressMonitor : IDisposable
    {
        /// <summary>(hid usage id, down) raised on the monitor's message thread.</summary>
        public event Action<int, bool> KeyEvent;

        public bool Available { get; private set; }
        public string LastError { get; private set; }
        public long EventCount { get { return Interlocked.Read(ref _eventCount); } }
        public bool IsRunning { get { return _running; } }

        private const int WM_INPUT = 0x00FF;
        private const int WM_QUIT = 0x0012;
        private const int WM_APP_LOOP_READY = 0x8001;       // WM_APP + 1 (thread message)
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIM_TYPEKEYBOARD = 1;
        private const ushort RI_KEY_BREAK = 0x01;
        private const ushort RI_KEY_E0 = 0x02;
        private const uint MAPVK_VK_TO_VSC_EX = 4;
        private const int UsageCount = 256;
        private const int VK_PAUSE = 0x13;
        private const int VK_PACKET = 0xE7;

        private static readonly int[] NormalKeys = BuildNormalKeys();
        private static readonly int[] ExtendedKeys = BuildExtendedKeys();

        private readonly object _startLock = new object();
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private readonly bool[] _down = new bool[UsageCount];

        private volatile bool _running;
        private bool _disposed;
        private Thread _thread;
        private uint _threadId;
        private IntPtr _hwnd;
        private RawWindow _window;
        private bool _startedOk;
        private string _startError;
        private IntPtr _rawBuffer;
        private int _rawCapacity;
        private long _eventCount;

        // ----------------------------------------------------------------- control
        /// <summary>Create the hidden window and start listening. Idempotent.</summary>
        public bool Start()
        {
            lock (_startLock)
            {
                if (_disposed)
                {
                    Available = false;
                    LastError = "键盘监听已释放";
                    return false;
                }
                if (_running) return Available;

                _startedOk = false;
                _startError = null;
                _ready.Reset();
                for (int i = 0; i < _down.Length; i++) _down[i] = false;

                Thread t = new Thread(ThreadProc);
                t.IsBackground = true;
                t.Name = "raw-input";
                t.Priority = ThreadPriority.AboveNormal;
                _thread = t;
                _running = true;
                try
                {
                    t.Start();
                }
                catch (Exception ex)
                {
                    _running = false;
                    _thread = null;
                    Available = false;
                    LastError = "键盘监听线程启动失败: " + ex.Message;
                    return false;
                }

                if (!_ready.WaitOne(3000))
                {
                    _running = false;
                    try { t.Join(1000); } catch { }
                    _thread = null;
                    Available = false;
                    LastError = "键盘监听初始化超时（RegisterRawInputDevices 未返回）";
                    return false;
                }

                if (!_startedOk)
                {
                    _running = false;
                    try { t.Join(1000); } catch { }
                    _thread = null;
                    Available = false;
                    LastError = _startError != null ? _startError : "键盘 Raw Input 注册失败";
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
                if (!_running && _thread == null)
                {
                    Available = false;
                    return;
                }

                _running = false;
                if (_threadId != 0)
                    Native.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

                Thread t = _thread;
                _thread = null;
                if (t != null && t != Thread.CurrentThread)
                {
                    try { t.Join(2000); } catch { }
                }
                Available = false;
                for (int i = 0; i < _down.Length; i++) _down[i] = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch { }
            if (_rawBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawBuffer);
                _rawBuffer = IntPtr.Zero;
                _rawCapacity = 0;
            }
            try { _ready.Close(); } catch { }
        }

        // ------------------------------------------------------------------ worker
        private void ThreadProc()
        {
            try
            {
                _threadId = Native.GetCurrentThreadId();

                RawWindow w = new RawWindow(this);
                CreateParams cp = new CreateParams();
                cp.Caption = "PrismSyncRawInput";
                cp.X = 0;
                cp.Y = 0;
                cp.Width = 0;
                cp.Height = 0;
                cp.Style = 0;
                cp.ExStyle = 0;
                w.CreateHandle(cp);
                _window = w;
                _hwnd = w.Handle;

                RawInputDevice[] devices = new RawInputDevice[1];
                devices[0].usUsagePage = 0x01;                          // generic desktop
                devices[0].usUsage = 0x06;                              // keyboard
                devices[0].dwFlags = RIDEV_INPUTSINK;                   // background delivery
                devices[0].hwndTarget = _hwnd;
                if (!Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice))))
                {
                    _startedOk = false;
                    _startError = "RegisterRawInputDevices 失败: Win32 错误 " + Marshal.GetLastWin32Error();
                    _ready.Set();
                    CleanupWindow();
                    return;
                }

                _startedOk = true;
                // Tell Start() only once we are actually pumping messages.
                Native.PostThreadMessage(_threadId, WM_APP_LOOP_READY, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _startedOk = false;
                _startError = "键盘监听初始化异常: " + ex.Message;
                _ready.Set();
                CleanupWindow();
                return;
            }

            try
            {
                Message msg;
                while (_running)
                {
                    int got = Native.GetMessage(out msg, IntPtr.Zero, 0, 0);
                    if (got <= 0) break;
                    if (msg.HWnd == IntPtr.Zero && msg.Msg == WM_APP_LOOP_READY)
                    {
                        _ready.Set();
                        continue;
                    }
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessage(ref msg);
                }
            }
            catch { }
            finally
            {
                CleanupWindow();
            }
        }

        private void CleanupWindow()
        {
            RawWindow w = _window;
            _window = null;
            _hwnd = IntPtr.Zero;
            if (w != null)
            {
                try { w.ReleaseHandle(); } catch { }
            }
        }

        // -------------------------------------------------------------- raw input
        private void HandleRawInput(IntPtr lParam)
        {
            uint size = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RawInputHeader));
            if (Native.GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize) == uint.MaxValue)
                return;
            if (size == 0) return;

            if (_rawCapacity < (int)size)
            {
                if (_rawBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_rawBuffer);
                _rawBuffer = Marshal.AllocHGlobal((int)size);
                _rawCapacity = (int)size;
                if (_rawBuffer == IntPtr.Zero) { _rawCapacity = 0; return; }
            }

            if (Native.GetRawInputData(lParam, RID_INPUT, _rawBuffer, ref size, headerSize) == uint.MaxValue)
                return;

            RawInputHeader header = (RawInputHeader)Marshal.PtrToStructure(_rawBuffer, typeof(RawInputHeader));
            if (header.dwType != RIM_TYPEKEYBOARD) return;

            int offset = Marshal.SizeOf(typeof(RawInputHeader));
            RawKeyboard kb = (RawKeyboard)Marshal.PtrToStructure(
                new IntPtr(_rawBuffer.ToInt64() + offset), typeof(RawKeyboard));

            int usage = UsageFromRaw(ref kb);
            if (usage <= 0 || usage >= UsageCount) return;

            bool down = (kb.Flags & RI_KEY_BREAK) == 0;
            if (down == _down[usage]) return;           // auto-repeat / duplicated event
            _down[usage] = down;
            RaiseKey(usage, down);
        }

        private void RaiseKey(int usage, bool down)
        {
            Interlocked.Increment(ref _eventCount);
            Action<int, bool> h = KeyEvent;
            if (h == null) return;
            try { h(usage, down); } catch { }
        }

        // ------------------------------------------------------------- VK -> usage
        private static int UsageFromRaw(ref RawKeyboard kb)
        {
            int vk = kb.VKey;
            if (vk == 0xFF || vk == VK_PACKET) return 0;    // fake / injected-packet pseudo key
            if (vk == VK_PAUSE) return 0x48;                // Pause is E1-encoded; scan code collides with NumLock

            int code = kb.MakeCode & 0xFF;
            bool extended = (kb.Flags & RI_KEY_E0) != 0;
            int usage = Lookup(code, extended);
            if (usage != 0) return usage;

            // Some keys arrive with no make code in the raw packet: ask the layout.
            if (vk != 0)
            {
                uint scan = Native.MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC_EX);
                if (scan != 0)
                {
                    usage = Lookup((int)(scan & 0xFF), (scan & 0xFF00) == 0xE000);
                }
            }
            return usage;
        }

        private static int Lookup(int scanCode, bool extended)
        {
            if (scanCode < 0 || scanCode > 255) return 0;
            return extended ? ExtendedKeys[scanCode] : NormalKeys[scanCode];
        }

        private static int[] BuildNormalKeys()
        {
            int[] t = new int[256];

            // A..Z (HID usage 0x04..0x1D)
            t[0x1E] = 0x04; t[0x30] = 0x05; t[0x2E] = 0x06; t[0x20] = 0x07;
            t[0x12] = 0x08; t[0x21] = 0x09; t[0x22] = 0x0A; t[0x23] = 0x0B;
            t[0x17] = 0x0C; t[0x24] = 0x0D; t[0x25] = 0x0E; t[0x26] = 0x0F;
            t[0x32] = 0x10; t[0x31] = 0x11; t[0x18] = 0x12; t[0x19] = 0x13;
            t[0x10] = 0x14; t[0x13] = 0x15; t[0x1F] = 0x16; t[0x14] = 0x17;
            t[0x16] = 0x18; t[0x2F] = 0x19; t[0x11] = 0x1A; t[0x2D] = 0x1B;
            t[0x15] = 0x1C; t[0x2C] = 0x1D;

            // 1..0
            t[0x02] = 0x1E; t[0x03] = 0x1F; t[0x04] = 0x20; t[0x05] = 0x21;
            t[0x06] = 0x22; t[0x07] = 0x23; t[0x08] = 0x24; t[0x09] = 0x25;
            t[0x0A] = 0x26; t[0x0B] = 0x27;

            // Enter, Esc, Backspace, Tab, Space, - = [ ] \ ; ' ` , . /
            t[0x1C] = 0x28; t[0x01] = 0x29; t[0x0E] = 0x2A; t[0x0F] = 0x2B;
            t[0x39] = 0x2C; t[0x0C] = 0x2D; t[0x0D] = 0x2E; t[0x1A] = 0x2F;
            t[0x1B] = 0x30; t[0x2B] = 0x31; t[0x27] = 0x33; t[0x28] = 0x34;
            t[0x29] = 0x35; t[0x33] = 0x36; t[0x34] = 0x37; t[0x35] = 0x38;
            t[0x3A] = 0x39;                                         // CapsLock

            // F1..F12
            t[0x3B] = 0x3A; t[0x3C] = 0x3B; t[0x3D] = 0x3C; t[0x3E] = 0x3D;
            t[0x3F] = 0x3E; t[0x40] = 0x3F; t[0x41] = 0x40; t[0x42] = 0x41;
            t[0x43] = 0x42; t[0x44] = 0x43; t[0x57] = 0x44; t[0x58] = 0x45;

            // ScrollLock, NumLock
            t[0x46] = 0x47;
            t[0x45] = 0x53;

            // Keypad (non-extended)
            t[0x37] = 0x55;                                         // KP *
            t[0x4A] = 0x56;                                         // KP -
            t[0x4E] = 0x57;                                         // KP +
            t[0x4F] = 0x59; t[0x50] = 0x5A; t[0x51] = 0x5B;         // KP 1,2,3
            t[0x4B] = 0x5C; t[0x4C] = 0x5D; t[0x4D] = 0x5E;         // KP 4,5,6
            t[0x47] = 0x5F; t[0x48] = 0x60; t[0x49] = 0x61;         // KP 7,8,9
            t[0x52] = 0x62; t[0x53] = 0x63;                         // KP 0, KP .

            // Left-side modifiers and RightShift (0xE0..0xE5)
            t[0x1D] = 0xE0;                                         // LeftCtrl
            t[0x2A] = 0xE1;                                         // LeftShift
            t[0x38] = 0xE2;                                         // LeftAlt
            t[0x36] = 0xE5;                                         // RightShift

            // Non-US backslash, KP =, F13..F24
            t[0x56] = 0x64;
            t[0x59] = 0x67;
            t[0x64] = 0x68; t[0x65] = 0x69; t[0x66] = 0x6A; t[0x67] = 0x6B;
            t[0x68] = 0x6C; t[0x69] = 0x6D; t[0x6A] = 0x6E; t[0x6B] = 0x6F;
            t[0x6C] = 0x70; t[0x6D] = 0x71; t[0x6E] = 0x72; t[0x76] = 0x73;

            return t;
        }

        private static int[] BuildExtendedKeys()
        {
            int[] t = new int[256];

            t[0x1C] = 0x58;                                         // KP Enter
            t[0x1D] = 0xE4;                                         // RightCtrl
            t[0x35] = 0x54;                                         // KP /
            t[0x37] = 0x46;                                         // PrintScreen
            t[0x38] = 0xE6;                                         // RightAlt
            t[0x47] = 0x4A;                                         // Home
            t[0x48] = 0x52;                                         // Up
            t[0x49] = 0x4B;                                         // PageUp
            t[0x4B] = 0x50;                                         // Left
            t[0x4D] = 0x4F;                                         // Right
            t[0x4F] = 0x4D;                                         // End
            t[0x50] = 0x51;                                         // Down
            t[0x51] = 0x4E;                                         // PageDown
            t[0x52] = 0x49;                                         // Insert
            t[0x53] = 0x4C;                                         // Delete
            t[0x5B] = 0xE3;                                         // LeftGUI
            t[0x5C] = 0xE7;                                         // RightGUI
            t[0x5D] = 0x65;                                         // Application (menu)
            t[0x5E] = 0x66;                                         // Power

            return t;
        }

        // ------------------------------------------------------------- hidden window
        private sealed class RawWindow : NativeWindow
        {
            private readonly KeyPressMonitor _owner;
            public RawWindow(KeyPressMonitor owner) { _owner = owner; }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    try { _owner.HandleRawInput(m.LParam); } catch { }
                }
                // WM_INPUT must still reach DefWindowProc so the system can clean up.
                base.WndProc(ref m);
            }
        }

        // ------------------------------------------------------------------- Win32
        private static class Native
        {
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool RegisterRawInputDevices(RawInputDevice[] pRawInputDevices,
                                                                uint uiNumDevices, uint cbSize);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData,
                                                        ref uint pcbSize, uint cbSizeHeader);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            internal static extern int GetMessage(out Message lpMsg, IntPtr hWnd,
                                                  uint wMsgFilterMin, uint wMsgFilterMax);

            [DllImport("user32.dll")]
            internal static extern bool TranslateMessage(ref Message lpMsg);

            [DllImport("user32.dll")]
            internal static extern IntPtr DispatchMessage(ref Message lpMsg);

            [DllImport("user32.dll")]
            internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

            [DllImport("kernel32.dll")]
            internal static extern uint GetCurrentThreadId();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawKeyboard
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }
    }
}
