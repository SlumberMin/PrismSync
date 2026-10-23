// Console check for the capture-driven effects (Lead tool).
using System;
using System.Threading;
using PrismSync;

internal static class EffectProbe
{
    private static void Main()
    {
        ScreenSampler screen = new ScreenSampler();
        Thread.Sleep(700);
        screen.Grab();
        Console.WriteLine("screen avg=" + screen.Average() + " avail=" + screen.Available);
        Rgb[,] grid = new Rgb[4, 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                grid[x, y] = screen.Cell(x * 8, y * 5);
        Console.Write("cells:");
        for (int y = 0; y < 4; y++) { Console.Write(" "); for (int x = 0; x < 4; x++) Console.Write(grid[x, y] + ","); }
        Console.WriteLine();

        EffectContext ctx = new EffectContext();
        ctx.Screen = screen;

        IEffect e = EffectRegistry.Create("screen", 1.0, Rgb.Black);
        Frame f = new Frame();
        KeyLayout layout = new KeyLayout();
        for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = layout.GlobalX(i);
        f.LaptopX = 0.37; f.HeadsetX = 0.065;
        for (int n = 0; n < 30; n++)
        {
            f.T = n / 30.0;
            e.Render(f, ctx);
            Thread.Sleep(33);
        }
        Console.WriteLine("screen effect: laptop=" + f.Laptop + " headset=" + f.Headset +
                          " kbd[0]=" + f.Keyboard[0] + " kbd[34]=" + f.Keyboard[34] +
                          " kbd[67]=" + f.Keyboard[67]);
        screen.Dispose();
    }
}
