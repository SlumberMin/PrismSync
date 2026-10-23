// Per-effect keyboard packet count probe (Lead tool).
using System;
using PrismSync;

internal static class PacketProbe
{
    private static void Main()
    {
        KeyLayout layout = new KeyLayout();
        Frame f = new Frame();
        for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = layout.GlobalX(i);
        f.LaptopX = 0.37; f.HeadsetX = 0.065;
        EffectContext ctx = new EffectContext();
        foreach (EffectSpec spec in EffectRegistry.Specs)
        {
            IEffect e = EffectRegistry.Create(spec.Id, 1.0, new Rgb(255, 0, 0));
            int max = 0, total = 0, samples = 0;
            for (int n = 0; n < 20; n++)
            {
                f.T = 0.7 + n * 0.13;
                e.Render(f, ctx);
                int packets = CountPackets(f, 16);
                if (packets > max) max = packets;
                total += packets;
                samples++;
            }
            Console.WriteLine(spec.Id.PadRight(9) + " max=" + max.ToString().PadLeft(3) +
                              " avg=" + ((double)total / samples).ToString("0.0").PadLeft(5));
        }
    }

    private static int CountPackets(Frame f, int bands)
    {
        int step = Math.Max(1, 255 / bands);
        System.Collections.Generic.Dictionary<int, int> groups =
            new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < Frame.KeyCount; i++)
        {
            int r = (f.Keyboard[i].R / step) * step;
            int g = (f.Keyboard[i].G / step) * step;
            int b = (f.Keyboard[i].B / step) * step;
            int key = (r << 16) | (g << 8) | b;
            int count;
            groups.TryGetValue(key, out count);
            groups[key] = count + 1;
        }
        int packets = 0;
        foreach (System.Collections.Generic.KeyValuePair<int, int> kv in groups)
            packets += (kv.Value + 51) / 52;
        return packets;
    }
}
