// keyinput_test.cs - standalone test for native/src/Capture/KeyPressMonitor.cs.
//
// Starts the raw-input monitor, injects keys with SendInput (letters, modifiers,
// arrows, function keys, keypad-free punctuation, auto-repeat duplicate) and
// verifies the HID usage ids reported through KeyEvent.
//
// Build (from the repo root):
//   csc /nologo /target:exe /platform:x64 /codepage:65001 /out:%TEMP%\keyinput_test.exe ^
//       /r:System.Windows.Forms.dll native\tools\keyinput_test.cs native\src\Capture\KeyPressMonitor.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PrismSync;

internal static class KeyInputTest
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private sealed class Ev
    {
        public int Usage;
        public bool Down;
        public Ev(int usage, bool down) { Usage = usage; Down = down; }
    }

    private static readonly object _lock = new object();
    private static readonly List<Ev> _events = new List<Ev>();

    private static int Main()
    {
        int size = Marshal.SizeOf(typeof(INPUT));
        Console.WriteLine("[key] Marshal.SizeOf(INPUT)=" + size + " (expect 40 on x64)");

        using (KeyPressMonitor monitor = new KeyPressMonitor())
        {
            monitor.KeyEvent += delegate(int usage, bool down)
            {
                lock (_lock) { _events.Add(new Ev(usage, down)); }
            };

            bool started = monitor.Start();
            Console.WriteLine("[key] Start()=" + started + " Available=" + monitor.Available +
                              " LastError=" + Text(monitor.LastError));
            if (!started || !monitor.Available) return 2;

            Thread.Sleep(200);                          // let the message loop settle

            // key, expected HID usage id
            int[] vks = new int[] { 0x41, 0xA0, 0x25, 0x1B, 0x74, 0x31, 0x20, 0x0D, 0x08, 0x11 };
            int[] expect = new int[] { 4, 225, 80, 41, 62, 30, 44, 40, 42, 26 };
            string[] names = new string[] { "A", "LeftShift", "Left", "Esc", "F5", "1", "Space", "Enter", "Backspace", "W" };

            for (int i = 0; i < vks.Length; i++)
            {
                SendKey((ushort)vks[i]);
                Thread.Sleep(45);
            }

            // Auto-repeat / duplicate suppression: two downs in a row must yield one event.
            Send((ushort)0x42, false);                  // B down
            Send((ushort)0x42, false);                  // B down again (duplicate)
            Thread.Sleep(45);
            Send((ushort)0x42, true);                   // B up
            Thread.Sleep(300);

            bool availableAfter = monitor.Available;
            monitor.Stop();
            Console.WriteLine("[key] After Stop(): Available=" + monitor.Available + " EventCount=" + monitor.EventCount +
                              " (was Available=" + availableAfter + ")");

            List<Ev> observed;
            lock (_lock) { observed = new List<Ev>(_events); }

            Console.WriteLine("[key] observed " + observed.Count + " events:");
            for (int i = 0; i < observed.Count; i++)
                Console.WriteLine("[key]   #" + i + " usage=" + observed[i].Usage + " " + (observed[i].Down ? "DOWN" : "UP"));

            int exit = 0;
            bool ok = true;
            if (!availableAfter)
            {
                Console.WriteLine("[key] FAIL monitor was not Available while running");
                ok = false;
            }

            // Verify each injected key produced the expected down+up pair.
            int index = 0;
            for (int i = 0; i < vks.Length; i++)
            {
                bool downOk = index < observed.Count && observed[index].Usage == expect[i] && observed[index].Down;
                bool upOk = index + 1 < observed.Count && observed[index + 1].Usage == expect[i] && !observed[index + 1].Down;
                if (!downOk || !upOk)
                {
                    Console.WriteLine("[key] FAIL " + names[i] + ": expected usage " + expect[i] + " down+up at #" + index);
                    ok = false;
                }
                index += 2;
            }

            // B: exactly one down and one up, despite the duplicated down.
            bool bDown = index < observed.Count && observed[index].Usage == 5 && observed[index].Down;
            bool bUp = index + 1 < observed.Count && observed[index + 1].Usage == 5 && !observed[index + 1].Down;
            if (!bDown || !bUp)
            {
                Console.WriteLine("[key] FAIL auto-repeat dedupe: expected one B down+up at #" + index);
                ok = false;
            }
            index += 2;

            int bDowns = 0;
            for (int i = 0; i < observed.Count; i++)
                if (observed[i].Usage == 5 && observed[i].Down) bDowns++;
            if (bDowns != 1)
            {
                Console.WriteLine("[key] FAIL duplicate B down produced " + bDowns + " down events (expect 1)");
                ok = false;
            }
            if (observed.Count != index)
            {
                Console.WriteLine("[key] FAIL event count " + observed.Count + " != expected " + index +
                                  " (extra keys pressed during the test?)");
                ok = false;
            }

            if (!ok) exit = 1;
            Console.WriteLine(exit == 0 ? "[key] RESULT PASS" : "[key] RESULT FAIL");
            return exit;
        }
    }

    private static void SendKey(ushort vk)
    {
        Send(vk, false);
        Send(vk, true);
    }

    private static void Send(ushort vk, bool up)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki = new KEYBDINPUT();
        inputs[0].u.ki.wVk = vk;
        inputs[0].u.ki.wScan = 0;
        inputs[0].u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        inputs[0].u.ki.time = 0;
        inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;
        uint sent = SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != 1)
            Console.WriteLine("[key] SendInput failed vk=0x" + vk.ToString("X2") + " err=" + Marshal.GetLastWin32Error());
    }

    private static string Text(string s)
    {
        return s == null ? "-" : s;
    }
}
