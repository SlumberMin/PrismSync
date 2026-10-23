// PrismSync native app - PREVIEW ONLY stubs for the two capture services.
//
// fx_preview.exe is compiled from Core/{Rgb,Frame,IEffect,EffectRegistry,
// Effects,KeyLayout}.cs + this file + fx_preview.cs. It deliberately does NOT
// link the real native/src/Capture implementations, so the preview is fully
// deterministic and needs no screen / microphone / elevated permissions.
//
// The public surface matches native/src/Capture (see CaptureStubs.cs in the
// original tree) so the effect code compiles against either variant.
using System;

namespace PrismSync
{
    /// <summary>Deterministic fake 32x18 desktop used by the "screen" effect preview.</summary>
    public sealed class ScreenSampler : IDisposable
    {
        public const int Cols = 32;
        public const int Rows = 18;

        private int _frame;

        public bool Available { get; private set; }
        public string LastError { get; private set; }

        public ScreenSampler()
        {
            Available = true;
            LastError = "";
        }

        public bool Grab() { _frame++; return true; }

        /// <summary>
        /// A synthetic "desktop": dark blue wallpaper, a warm window on the left,
        /// a teal window in the middle and a magenta app on the right, plus a
        /// grey taskbar. The hues drift slowly with every Grab so the smooth
        /// filter of the effect is visible in the preview strip.
        /// </summary>
        public Rgb Cell(int x, int y)
        {
            double u = x / (double)(Cols - 1);
            double v = y / (double)(Rows - 1);
            double drift = _frame * 0.015;

            // wallpaper: deep blue at the top, purple near the bottom
            double h = 0.62 + 0.05 * v + 0.01 * Math.Sin(u * 3.0 + drift);
            Rgb c = Rgb.FromHsv(h, 0.80, 0.12 + 0.20 * v);

            // warm "document window" on the left third
            if (u > 0.06 && u < 0.36 && v > 0.12 && v < 0.62)
                c = Rgb.FromHsv(0.08 + 0.02 * Math.Sin(drift), 0.72, 0.78);

            // bright cyan "browser" in the middle
            if (u > 0.44 && u < 0.66 && v > 0.20 && v < 0.74)
                c = Rgb.FromHsv(0.50, 0.65, 0.86);

            // magenta player on the right
            if (u > 0.74 && u < 0.92 && v > 0.08 && v < 0.80)
                c = Rgb.FromHsv(0.88, 0.70, 0.72);

            // grey taskbar at the bottom
            if (v > 0.92) c = new Rgb(78, 82, 90);

            // window title bars / edges for a bit of structure
            if ((u > 0.06 && u < 0.36 && v > 0.12 && v < 0.16) ||
                (u > 0.44 && u < 0.66 && v > 0.20 && v < 0.24) ||
                (u > 0.74 && u < 0.92 && v > 0.08 && v < 0.12))
                c = Rgb.FromHsv(0.60, 0.10, 0.55);

            return c;
        }

        public Rgb Average()
        {
            long r = 0, g = 0, b = 0;
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                {
                    Rgb c = Cell(x, y);
                    r += c.R; g += c.G; b += c.B;
                }
            int n = Cols * Rows;
            return new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }

        public void Dispose() { }
    }

    /// <summary>Fake microphone meter: the preview tool writes the level fields.</summary>
    public sealed class AudioMeter : IDisposable
    {
        public bool Available { get; private set; }
        public string LastError { get; private set; }
        public float Level { get; set; }
        public float Bass { get; set; }
        public float Mid { get; set; }
        public float Treble { get; set; }
        public bool Beat { get; set; }

        public AudioMeter()
        {
            Available = true;
            LastError = "";
        }

        public bool Start() { return true; }
        public void Stop() { }
        public void Dispose() { }
    }
}
