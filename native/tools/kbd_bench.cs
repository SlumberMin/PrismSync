// Benchmark per-frame keyboard apply cost (Lead tool).
using System;
using System.Diagnostics;
using System.Threading;
using PrismSync;

internal static class KbdBench
{
    private static void Main(string[] args)
    {
        int mode = args.Length > 0 ? int.Parse(args[0]) : 0;   // 0 = gradient, 1 = solid
        int bands = args.Length > 1 ? int.Parse(args[1]) : 0;
        KeyLayout layout = new KeyLayout();
        Hero68Device kbd = new Hero68Device(layout);
        kbd.Bands = bands;
        Console.WriteLine("open=" + kbd.Open() + " err=" + kbd.LastError);
        Frame f = new Frame();
        Stopwatch sw = Stopwatch.StartNew();
        double total = 0, max = 0;
        int frames = 30;
        for (int n = 0; n < frames; n++)
        {
            double t = n / 30.0;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                if (mode == 1) f.Keyboard[i] = new Rgb(0, 255, 0);
                else f.Keyboard[i] = Rgb.FromHsv(0.6 + layout.LocalX[i] * 0.4 + t * 0.05, 1.0, 1.0);
            }
            long t0 = Stopwatch.GetTimestamp();
            kbd.Apply(f);
            double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            total += ms;
            if (ms > max) max = ms;
            Thread.Sleep(33);
        }
        Console.WriteLine("mode=" + mode + " bands=" + bands + " frames=" + frames +
                          " avg=" + (total / frames).ToString("0.00") + "ms max=" +
                          max.ToString("0.00") + "ms applied=" + kbd.Applied +
                          " failed=" + kbd.Failed);
        kbd.Close();
    }
}
