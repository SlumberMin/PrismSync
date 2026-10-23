// Console hardware check for the native adapters (Lead tool).
using System;
using System.Threading;
using PrismSync;

internal static class DevTest
{
    private static void Main(string[] args)
    {
        KeyLayout layout = new KeyLayout();
        Console.WriteLine("layout: " + layout.Source + "  rows=" + layout.RowCount);

        Hero68Device kbd = new Hero68Device(layout);
        MchoseDevice hs = new MchoseDevice(0);
        ClevoDevice lap = new ClevoDevice();

        Console.WriteLine("open keyboard -> " + kbd.Open() + "  err=" + kbd.LastError +
                          "  uid=" + kbd.Uid);
        Console.WriteLine("open headset  -> " + hs.Open() + "  err=" + hs.LastError);
        Console.WriteLine("open laptop   -> " + lap.Open() + "  err=" + lap.LastError);
        Console.WriteLine("headset probe: " + hs.Probe());

        Frame f = new Frame();
        DateTime start = DateTime.UtcNow;
        int frames = 0;
        for (int n = 0; n < 90; n++)
        {
            double t = (DateTime.UtcNow - start).TotalSeconds;
            f.T = t;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                double x = 0.60 + layout.LocalX[i] * 0.40;
                f.Keyboard[i] = Rgb.FromHsv(x + t * 0.1, 1.0, 1.0);
            }
            f.Headset = Rgb.FromHsv(t * 0.15, 1.0, 1.0);
            f.Laptop = f.Headset;
            kbd.Apply(f);
            hs.Apply(f);
            lap.Apply(f);
            frames++;
            Thread.Sleep(33);
        }
        Console.WriteLine("applied frames=" + frames);
        Console.WriteLine("keyboard applied=" + kbd.Applied + " failed=" + kbd.Failed +
                          " err=" + kbd.LastError);
        Console.WriteLine("headset  applied=" + hs.Applied + " failed=" + hs.Failed +
                          " acks=" + hs.Acks + " err=" + hs.LastError);
        Console.WriteLine("laptop   applied=" + lap.Applied + " failed=" + lap.Failed +
                          " err=" + lap.LastError);
        Console.WriteLine("headset probe after stream: " + hs.Probe());

        kbd.Close();
        hs.Close();
        lap.Close();
        Console.WriteLine("closed");
    }
}
