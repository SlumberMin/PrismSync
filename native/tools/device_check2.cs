// Verify the headset now really changes colour (0x07 readback) and that the
// keyboard dirty-update path keeps the packet count bounded (Lead tool).
using System;
using System.Threading;
using PrismSync;

internal static class DeviceCheck2
{
    private static void Main(string[] args)
    {
        KeyLayout layout = new KeyLayout();
        bool onlyKbd = args.Length > 0 && args[0] == "kbd";
        if (!onlyKbd)
        {
            Console.WriteLine("== headset ==");
            MchoseDevice hs = new MchoseDevice(0);
            Console.WriteLine("open=" + hs.Open() + " err=" + hs.LastError + " probe=" + hs.Probe());
            Frame hf = new Frame();
            for (int i = 0; i < Frame.KeyCount; i++) hf.Keyboard[i] = Rgb.Black;
            hf.Headset = new Rgb(255, 0, 40);
            hf.Laptop = hf.Headset;
            for (int i = 0; i < 8; i++) { hs.Apply(hf); Thread.Sleep(60); }
            Thread.Sleep(150);
            Console.WriteLine("after red  applied=" + hs.Applied + " failed=" + hs.Failed +
                              " probe=" + hs.Probe());
            hf.Headset = new Rgb(0, 40, 255);
            for (int i = 0; i < 8; i++) { hs.Apply(hf); Thread.Sleep(60); }
            Thread.Sleep(150);
            Console.WriteLine("after blue probe=" + hs.Probe());
            hs.Close();
            Console.WriteLine();
        }

        int hueSteps = args.Length > 1 ? int.Parse(args[1]) : 20;
        int max = args.Length > 2 ? int.Parse(args[2]) : 16;
        Console.WriteLine("== keyboard dirty updates (hueSteps=" + hueSteps + " max=" + max + ") ==");
        Hero68Device kbd = new Hero68Device(layout);
        kbd.UseHsvQuant = true;
        kbd.HueSteps = hueSteps;
        kbd.MaxPacketsPerFrame = max;
        Console.WriteLine("open=" + kbd.Open() + " err=" + kbd.LastError);
        Frame f = new Frame();
        double hue = 0;
        int maxPackets = 0;
        long totalPackets = 0;
        for (int n = 0; n < 150; n++)
        {
            for (int i = 0; i < Frame.KeyCount; i++)
                f.Keyboard[i] = Rgb.FromHsv(hue + layout.LocalX[i] * 2.5, 1.0, 1.0);
            kbd.Apply(f);
            if (kbd.LastPackets > maxPackets) maxPackets = kbd.LastPackets;
            totalPackets += kbd.LastPackets;
            hue += 0.006;
            Thread.Sleep(33);
            if (n % 30 == 0)
                Console.WriteLine("frame " + n + " packets=" + kbd.LastPackets +
                                  " apply=" + kbd.LastApplyMs.ToString("0.00") + "ms");
        }
        Console.WriteLine("max packets/frame=" + maxPackets + " avg=" +
                          ((double)totalPackets / 150).ToString("0.0") +
                          " applied=" + kbd.Applied + " failed=" + kbd.Failed);
        kbd.Close();
    }
}
