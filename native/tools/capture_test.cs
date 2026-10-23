// capture_test.cs - standalone test for native/src/Capture/ScreenSampler.cs.
//
// Verifies the cached sampling contract: Grab() must stay far below 2 ms, the
// background worker must refresh while the effect is being drawn, and the worker
// must go idle when nobody calls Grab(). Colours are compared against an
// independent GDI CopyFromScreen box-downscale and a visual PNG is written.
//
// Build (from the repo root):
//   csc /nologo /target:exe /platform:x64 /codepage:65001 /out:%TEMP%\capture_test.exe ^
//       /r:System.Drawing.dll native\tools\capture_test.cs ^
//       native\src\Capture\ScreenSampler.cs native\src\Core\Rgb.cs
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PrismSync;

internal static class CaptureTest
{
    private static int Main()
    {
        string pngPath = Path.Combine(Path.GetTempPath(), "shenguang_capture.png");
        int exit = 0;

        using (ScreenSampler sampler = new ScreenSampler())
        {
            Console.WriteLine("[capture] Available=" + sampler.Available + " LastError=" + Text(sampler.LastError));
            if (!sampler.Available) return 3;
            Console.WriteLine("[capture] source=" + sampler.SourceWidth + "x" + sampler.SourceHeight +
                              " refreshInterval=" + sampler.RefreshIntervalMs + " ms");

            bool first = sampler.Grab();
            Console.WriteLine("[capture] first Grab=" + first + " HasData=" + sampler.HasData +
                              " LastError=" + Text(sampler.LastError));
            if (!first) return 3;

            // ---------------------------------------------------------- Grab() cost
            const int grabs = 500;
            double min = double.MaxValue, max = 0.0, sum = 0.0;
            int over2ms = 0;
            for (int i = 0; i < grabs; i++)
            {
                if (!sampler.Grab())
                {
                    Console.WriteLine("[capture] Grab #" + i + " failed: " + Text(sampler.LastError));
                    return 3;
                }
                double ms = sampler.LastGrabMs;
                sum += ms;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
                if (ms >= 2.0) over2ms++;
            }
            double avgGrab = sum / grabs;
            Console.WriteLine("[capture] Grab x" + grabs + " avg=" + avgGrab.ToString("F4") + " ms min=" +
                              min.ToString("F4") + " max=" + max.ToString("F4") + " over2ms=" + over2ms +
                              " (target < 2 ms)");
            if (over2ms > 0 || avgGrab >= 2.0)
            {
                Console.WriteLine("[capture] FAIL Grab() is not below 2 ms");
                exit = 6;
            }

            // ------------------------------------------------- worker read cost
            Console.WriteLine("[capture] real screen reads=" + sampler.CaptureCount +
                              " last=" + sampler.LastCaptureMs.ToString("F3") + " ms" +
                              " avg=" + sampler.AverageCaptureMs.ToString("F3") + " ms" +
                              " frameAge=" + sampler.DataAgeMs.ToString("F1") + " ms");

            // ------------------------------------- forced fresh frame + reference
            sampler.RefreshIntervalMs = 2000;             // keep the worker away from the comparison
            long t0 = Stopwatch.GetTimestamp();
            bool refreshed = sampler.Refresh();
            double refreshMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine("[capture] Refresh()=" + refreshed + " cost=" + refreshMs.ToString("F3") + " ms");

            Rgb samplerAverage = sampler.Average();
            int w = sampler.SourceWidth;
            int h = sampler.SourceHeight;
            using (Bitmap screen = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(screen))
                    g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));

                Rgb[,] reference = BoxAverage(screen, w, h);
                long refR = 0, refG = 0, refB = 0;
                double diffSum = 0.0;
                int maxDiff = 0;
                int bad = 0;
                for (int y = 0; y < ScreenSampler.Rows; y++)
                {
                    for (int x = 0; x < ScreenSampler.Cols; x++)
                    {
                        Rgb a = sampler.Cell(x, y);
                        Rgb b = reference[x, y];
                        int dr = Math.Abs(a.R - b.R);
                        int dg = Math.Abs(a.G - b.G);
                        int db = Math.Abs(a.B - b.B);
                        int d = Math.Max(dr, Math.Max(dg, db));
                        diffSum += (dr + dg + db) / 3.0;
                        if (d > maxDiff) maxDiff = d;
                        if (d > 64) bad++;
                        refR += b.R;
                        refG += b.G;
                        refB += b.B;
                    }
                }
                int cells = ScreenSampler.Cols * ScreenSampler.Rows;
                double meanDiff = diffSum / cells;
                Rgb referenceAverage = new Rgb((byte)((refR + cells / 2) / cells),
                                               (byte)((refG + cells / 2) / cells),
                                               (byte)((refB + cells / 2) / cells));
                Console.WriteLine("[capture] colour check vs CopyFromScreen box average: mean|d|=" + meanDiff.ToString("F2") +
                                  " max|d|=" + maxDiff + " cells>64=" + bad + "/" + cells);
                Console.WriteLine("[capture] average colour: sampler=" + Hex(samplerAverage) +
                                  " reference=" + Hex(referenceAverage));
                if (meanDiff > 12.0 || maxDiff > 96)
                {
                    Console.WriteLine("[capture] FAIL sampled colours differ too much from the reference");
                    exit = 4;
                }

                PrintCell(sampler, reference, 0, 0);
                PrintCell(sampler, reference, ScreenSampler.Cols - 1, 0);
                PrintCell(sampler, reference, 0, ScreenSampler.Rows - 1);
                PrintCell(sampler, reference, ScreenSampler.Cols - 1, ScreenSampler.Rows - 1);
                PrintCell(sampler, reference, ScreenSampler.Cols / 2, ScreenSampler.Rows / 2);

                bool uniform = true;
                Rgb firstCell = sampler.Cell(0, 0);
                for (int y = 0; y < ScreenSampler.Rows && uniform; y++)
                    for (int x = 0; x < ScreenSampler.Cols && uniform; x++)
                        if (!Rgb.Same(sampler.Cell(x, y), firstCell)) uniform = false;
                Console.WriteLine("[capture] frame is uniform=" + uniform + " (expect False on a real desktop)");
                if (uniform)
                {
                    Console.WriteLine("[capture] FAIL every cell has the same colour");
                    exit = 4;
                }

                SaveVisual(sampler, screen, pngPath);
                Console.WriteLine("[capture] PNG=" + pngPath);
            }

            // ------------------------------------- worker refreshes while in use
            sampler.RefreshIntervalMs = 200;
            long beforeBusy = sampler.CaptureCount;
            for (int i = 0; i < 25; i++)
            {
                sampler.Grab();
                Thread.Sleep(40);                        // ~1 s of 25 fps rendering
            }
            long afterBusy = sampler.CaptureCount;
            Console.WriteLine("[capture] worker while rendering: reads " + beforeBusy + " -> " + afterBusy +
                              " (expect ~5 more)");
            if (afterBusy <= beforeBusy)
            {
                Console.WriteLine("[capture] FAIL worker did not refresh while Grab() was being called");
                exit = 6;
            }

            // ------------------------------------- worker goes idle when unused
            Thread.Sleep(1300);                          // > 1 s after the last Grab()
            long idleStart = sampler.CaptureCount;
            Thread.Sleep(800);
            long idleEnd = sampler.CaptureCount;
            Console.WriteLine("[capture] worker while idle: reads " + idleStart + " -> " + idleEnd +
                              " (expect equal)");
            if (idleEnd != idleStart)
            {
                Console.WriteLine("[capture] FAIL worker kept reading the screen with no consumer");
                exit = 6;
            }

            // Out-of-range cells must stay black instead of throwing.
            if (!Rgb.Same(sampler.Cell(-1, 0), Rgb.Black) || !Rgb.Same(sampler.Cell(0, 9999), Rgb.Black))
            {
                Console.WriteLine("[capture] FAIL out-of-range Cell() did not return black");
                exit = 4;
            }
        }

        // Dispose must be idempotent and Grab() must fail cleanly afterwards.
        ScreenSampler dead = new ScreenSampler();
        dead.Dispose();
        bool afterDispose = dead.Grab();
        Console.WriteLine("[capture] Grab after Dispose=" + afterDispose + " (expect False) LastError=" + Text(dead.LastError));
        dead.Dispose();
        if (afterDispose)
        {
            Console.WriteLine("[capture] FAIL Grab succeeded after Dispose");
            exit = 5;
        }

        Console.WriteLine(exit == 0 ? "[capture] RESULT PASS" : "[capture] RESULT FAIL code=" + exit);
        return exit;
    }

    // ---------------------------------------------------------------- reference
    private static Rgb[,] BoxAverage(Bitmap bmp, int w, int h)
    {
        int cols = ScreenSampler.Cols;
        int rows = ScreenSampler.Rows;
        int cells = cols * rows;
        long[] sr = new long[cells];
        long[] sg = new long[cells];
        long[] sb = new long[cells];
        int[] counts = new int[cells];
        int[] colMap = new int[w];
        for (int x = 0; x < w; x++) colMap[x] = x * cols / w;

        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] rowPixels = new int[w];
            for (int y = 0; y < h; y++)
            {
                IntPtr line = new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride);
                Marshal.Copy(line, rowPixels, 0, w);
                int baseIdx = (y * rows / h) * cols;
                for (int x = 0; x < w; x++)
                {
                    int argb = rowPixels[x];
                    int idx = baseIdx + colMap[x];
                    sr[idx] += (argb >> 16) & 0xFF;
                    sg[idx] += (argb >> 8) & 0xFF;
                    sb[idx] += argb & 0xFF;
                    counts[idx]++;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        Rgb[,] result = new Rgb[cols, rows];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                int idx = y * cols + x;
                int c = counts[idx] > 0 ? counts[idx] : 1;
                result[x, y] = new Rgb((byte)((sr[idx] + c / 2) / c),
                                       (byte)((sg[idx] + c / 2) / c),
                                       (byte)((sb[idx] + c / 2) / c));
            }
        }
        return result;
    }

    private static void PrintCell(ScreenSampler s, Rgb[,] reference, int x, int y)
    {
        Rgb a = s.Cell(x, y);
        Rgb b = reference[x, y];
        Console.WriteLine(string.Format("[capture] cell({0,2},{1,2}) sampler={2} ref={3} dR={4} dG={5} dB={6}",
                                        x, y, Hex(a), Hex(b),
                                        Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    }

    // ------------------------------------------------------------------ visual
    private static void SaveVisual(ScreenSampler s, Bitmap reference, string path)
    {
        const int cell = 20;
        int rawW = ScreenSampler.Cols * cell;
        int rawH = ScreenSampler.Rows * cell;
        int width = rawW + 20;
        int height = 22 + rawH + 26 + rawH + 12;

        using (Bitmap dst = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.FromArgb(22, 22, 26));
                using (Font font = new Font("Microsoft YaHei", 9f))
                using (Brush textBrush = new SolidBrush(Color.FromArgb(220, 220, 230)))
                {
                    g.DrawString("ScreenSampler " + ScreenSampler.Cols + "x" + ScreenSampler.Rows + " (" +
                                 s.SourceWidth + "x" + s.SourceHeight + ", HALFTONE, cached)",
                                 font, textBrush, 10, 4);

                    int y0 = 22;
                    for (int y = 0; y < ScreenSampler.Rows; y++)
                    {
                        for (int x = 0; x < ScreenSampler.Cols; x++)
                        {
                            Rgb c = s.Cell(x, y);
                            using (Brush b = new SolidBrush(Color.FromArgb(c.R, c.G, c.B)))
                                g.FillRectangle(b, 10 + x * cell, y0 + y * cell, cell, cell);
                        }
                    }
                    using (Pen grid = new Pen(Color.FromArgb(55, 255, 255, 255)))
                    {
                        for (int x = 0; x <= ScreenSampler.Cols; x++)
                            g.DrawLine(grid, 10 + x * cell, y0, 10 + x * cell, y0 + rawH);
                        for (int y = 0; y <= ScreenSampler.Rows; y++)
                            g.DrawLine(grid, 10, y0 + y * cell, 10 + rawW, y0 + y * cell);
                    }

                    g.DrawString("reference: CopyFromScreen box downscale", font, textBrush, 10, y0 + rawH + 8);
                    int y1 = y0 + rawH + 26;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(reference, new Rectangle(10, y1, rawW, rawH));

                    using (Pen edge = new Pen(Color.FromArgb(90, 255, 255, 255)))
                    {
                        g.DrawRectangle(edge, 10, y0, rawW - 1, rawH - 1);
                        g.DrawRectangle(edge, 10, y1, rawW - 1, rawH - 1);
                    }
                }
            }
            dst.Save(path, ImageFormat.Png);
        }
    }

    private static string Hex(Rgb c)
    {
        return c.ToHex();
    }

    private static string Text(string s)
    {
        return s == null ? "-" : s;
    }
}
