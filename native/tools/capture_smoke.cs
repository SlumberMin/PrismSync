// Console smoke test for the capture helpers (Lead tool).
using System;
using System.Threading;
using PrismSync;

internal static class CaptureSmoke
{
    private static void Main()
    {
        Console.WriteLine("== screen ==");
        ScreenSampler screen = new ScreenSampler();
        for (int i = 0; i < 20; i++)
        {
            screen.Grab();
            Thread.Sleep(50);
            if (screen.HasData) break;
        }
        Console.WriteLine("available=" + screen.Available + " hasData=" + screen.HasData +
                          " source=" + screen.SourceWidth + "x" + screen.SourceHeight +
                          " avg=" + screen.Average() + " err=" + screen.LastError);
        Rgb[,] cells = new Rgb[ScreenSampler.Cols, ScreenSampler.Rows];
        for (int y = 0; y < ScreenSampler.Rows; y++)
            for (int x = 0; x < ScreenSampler.Cols; x++)
                cells[x, y] = screen.Cell(x, y);
        Console.WriteLine("cells: " + cells[0, 0] + " " + cells[16, 9] + " " + cells[31, 17]);
        Console.WriteLine("grabMs=" + screen.LastGrabMs.ToString("0.00") +
                          " captureMs=" + screen.AverageCaptureMs.ToString("0.00"));
        screen.Dispose();

        Console.WriteLine();
        Console.WriteLine("== audio (3s) ==");
        AudioMeter mic = new AudioMeter();
        Console.WriteLine("start=" + mic.Start() + " err=" + mic.LastError +
                          " device=" + mic.DeviceName);
        if (mic.IsRunning)
        {
            float peak = 0, maxLevel = 0, minLevel = 1;
            int beats = 0;
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(100);
                if (mic.Level > maxLevel) maxLevel = mic.Level;
                if (mic.Level < minLevel) minLevel = mic.Level;
                if (mic.Peak > peak) peak = mic.Peak;
                if (mic.Beat) beats++;
            }
            Console.WriteLine("level min=" + minLevel.ToString("0.000") +
                              " max=" + maxLevel.ToString("0.000") +
                              " peak=" + peak.ToString("0.000") +
                              " beats=" + beats + " samples=" + mic.SampleCount);
        }
        mic.Stop();
        mic.Dispose();

        Console.WriteLine();
        Console.WriteLine("== raw keyboard ==");
        KeyPressMonitor keys = new KeyPressMonitor();
        int count = 0;
        int lastUsage = -1;
        keys.KeyEvent += delegate (int usage, bool down)
        {
            if (down) { count++; lastUsage = usage; Console.WriteLine("  key usage=0x" + usage.ToString("X2")); }
        };
        Console.WriteLine("start=" + keys.Start() + " err=" + keys.LastError);
        Thread.Sleep(200);
        for (int i = 0; i < 3; i++)
        {
            SendKey(0x41 + i);   // A, B, C
            Thread.Sleep(150);
        }
        Thread.Sleep(400);
        Console.WriteLine("events=" + count + " lastUsage=0x" + lastUsage.ToString("X2"));
        keys.Stop();
        keys.Dispose();
    }

    private static void SendKey(int vk)
    {
        keybd_event((byte)vk, 0, 0, IntPtr.Zero);
        Thread.Sleep(30);
        keybd_event((byte)vk, 0, 2, IntPtr.Zero);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
