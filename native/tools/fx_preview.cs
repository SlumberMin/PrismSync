// PrismSync native app - effect preview renderer / visual self-test (console).
//
// Build + run with:  pwsh -NoProfile -ExecutionPolicy Bypass -File native\tools\fx_preview.ps1
//
// It links the real effect library (Core/{Rgb,Frame,IEffect,EffectRegistry,
// Effects,KeyLayout}.cs) with the fake capture services in fx_preview_stubs.cs,
// runs every effect for 30 fps up to t = 0.7 / 2.3 / 4.1 s (so stateful effects
// such as screen/audio have converged exactly like in the app), then renders the
// whole desk - headset capsule, laptop, 67-key keyboard - to PNG.
//
// Output (native/tools/fx_previews/):
//   <id>_t070.png / _t230.png / _t410.png   one panel per effect + time
//   strips/<id>.png                          the three times stacked
//   contact_ported.png, contact_new.png      overview sheets (3 columns)
//   screen_fallback_t230.png, audio_fallback_t230.png
//   fx_preview_report.txt                    brightness / perf / determinism
//
// The report flags effects that are too dark (average luminance < 24 or more
// than 30 of 67 keys nearly black) or blown out, so the visual review has an
// objective baseline as well.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace PrismSync
{
    internal static class FxPreview
    {
        private const int PanelW = 1180;
        private const int PanelH = 360;
        private const double Fps = 30.0;
        private const double Dt = 1.0 / Fps;

        private static readonly double[] SampleTimes = new double[] { 0.7, 2.3, 4.1 };
        private static readonly Rgb BaseColor = new Rgb(255, 64, 48);

        // Deterministic keystroke schedule for the keyhit effect (desk positions).
        private static readonly double[] HitTimes = new double[]
        { 0.10, 0.42, 0.78, 1.05, 1.38, 1.72, 2.05, 2.44, 2.83, 3.15, 3.52, 3.86 };
        private static readonly double[] HitXs = new double[]
        { 0.66, 0.82, 0.74, 0.93, 0.63, 0.79, 0.88, 0.69, 0.76, 0.97, 0.71, 0.85 };

        private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font InfoFont = new Font("Consolas", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font LabelFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font SheetFont = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Pixel);

        private sealed class Sample
        {
            public Frame Frame;
            public double T;
            public double MsPerFrame;
        }

        private static void Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "fx_previews");
            string stripDir = Path.Combine(outDir, "strips");
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(stripDir);

            KeyLayout layout = new KeyLayout();
            Console.WriteLine("[preview] keylayout source=" + layout.Source + " rows=" + layout.RowCount);

            EffectSpec[] specs = EffectRegistry.Specs;
            Console.WriteLine("[preview] catalogue: " + specs.Length + " effects");
            StringBuilder report = new StringBuilder();
            report.AppendLine("PrismSync effect preview - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("catalog effects: " + specs.Length);
            report.AppendLine("keylayout source: " + layout.Source);
            report.AppendLine();
            report.AppendLine("id            t     avgLum  minLum  maxLum  blackKeys  distinct  laptop      headset     ms/frame  warnings");
            report.AppendLine("---------------------------------------------------------------------------------------------------------");

            int warnings = 0;
            int panelsWritten = 0;
            List<EffectSpec> ported = new List<EffectSpec>();
            List<EffectSpec> added = new List<EffectSpec>();
            Dictionary<string, Bitmap[]> thumbs = new Dictionary<string, Bitmap[]>();

            for (int si = 0; si < specs.Length; si++)
            {
                EffectSpec spec = specs[si];
                if (si < 10) ported.Add(spec); else added.Add(spec);

                Bitmap[] panels = new Bitmap[SampleTimes.Length];
                Bitmap[] small = new Bitmap[SampleTimes.Length];

                for (int ti = 0; ti < SampleTimes.Length; ti++)
                {
                    double t = SampleTimes[ti];
                    Sample s = Simulate(spec, layout, t, true);

                    Bitmap panel = DrawPanel(s, spec, t, layout);
                    string path = Path.Combine(outDir, spec.Id + "_t" + ((int)Math.Round(t * 100)).ToString("000") + ".png");
                    panel.Save(path, ImageFormat.Png);
                    panelsWritten++;
                    panels[ti] = panel;

                    Bitmap thumb = Thumbnail(panel, 360, 110);
                    small[ti] = thumb;

                    // ---- objective stats --------------------------------------
                    double avg, minL, maxL; int black; int distinct;
                    Stats(s.Frame, out avg, out minL, out maxL, out black, out distinct);
                    string warn = "";
                    if (avg < 24.0) { warn += "DARK "; warnings++; }
                    if (black > 30) { warn += "MOSTLY-BLACK "; warnings++; }
                    if (avg > 242.0) { warn += "BLOWN "; warnings++; }
                    if (ti == 0 && distinct < 4 && spec.Id != "static" && spec.Id != "breathe")
                        warn += "LOW-VARIANCE ";
                    report.AppendLine(string.Format("{0,-12}  {1:0.00}  {2,6:0.0}  {3,6:0.0}  {4,6:0.0}  {5,9}  {6,8}  {7,-11} {8,-11} {9,9:0.000}  {10}",
                        spec.Id, t, avg, minL, maxL, black, distinct,
                        s.Frame.Laptop.ToHex(), s.Frame.Headset.ToHex(), s.MsPerFrame, warn));
                }

                Bitmap strip = ComposeStrip(panels);
                strip.Save(Path.Combine(stripDir, spec.Id + ".png"), ImageFormat.Png);
                strip.Dispose();
                for (int i = 0; i < panels.Length; i++) { panels[i].Dispose(); }
                thumbs[spec.Id] = small;

                Console.WriteLine("[preview] " + spec.Id + " done (" + panelsWritten + " panels)");
            }

            // ---- fallback renders for the two service-dependent effects ------
            EffectSpec screenSpec = EffectRegistry.FindOrNull("screen");
            EffectSpec audioSpec = EffectRegistry.FindOrNull("audio");
            if (screenSpec != null)
            {
                Sample s = Simulate(screenSpec, layout, 2.3, false);
                Bitmap b = DrawPanel(s, screenSpec, 2.3, layout);
                b.Save(Path.Combine(outDir, "screen_fallback_t230.png"), ImageFormat.Png);
                b.Dispose();
                double avg, minL, maxL; int black, distinct;
                Stats(s.Frame, out avg, out minL, out maxL, out black, out distinct);
                report.AppendLine(string.Format("{0,-12}  {1:0.00}  {2,6:0.0}  {3,6:0.0}  {4,6:0.0}  {5,9}  {6,8}  {7,-11} {8,-11} {9,9:0.000}  {10}",
                    "screen[fb]", 2.3, avg, minL, maxL, black, distinct,
                    s.Frame.Laptop.ToHex(), s.Frame.Headset.ToHex(), s.MsPerFrame,
                    (avg < 24.0 ? "FALLBACK-DARK" : "fallback ok")));
                if (avg < 24.0) warnings++;
            }
            if (audioSpec != null)
            {
                Sample s = Simulate(audioSpec, layout, 2.3, false);
                Bitmap b = DrawPanel(s, audioSpec, 2.3, layout);
                b.Save(Path.Combine(outDir, "audio_fallback_t230.png"), ImageFormat.Png);
                b.Dispose();
                double avg, minL, maxL; int black, distinct;
                Stats(s.Frame, out avg, out minL, out maxL, out black, out distinct);
                report.AppendLine(string.Format("{0,-12}  {1:0.00}  {2,6:0.0}  {3,6:0.0}  {4,6:0.0}  {5,9}  {6,8}  {7,-11} {8,-11} {9,9:0.000}  {10}",
                    "audio[fb]", 2.3, avg, minL, maxL, black, distinct,
                    s.Frame.Laptop.ToHex(), s.Frame.Headset.ToHex(), s.MsPerFrame,
                    (avg < 24.0 ? "FALLBACK-DARK" : "fallback ok")));
                if (avg < 24.0) warnings++;
            }

            // ---- determinism + UsesColor flag checks -------------------------
            report.AppendLine();
            report.AppendLine("determinism / colour flag checks");
            report.AppendLine("id            deterministic  usesColor-matches");
            int deterministicBad = 0;
            int flagBad = 0;
            for (int si = 0; si < specs.Length; si++)
            {
                EffectSpec spec = specs[si];
                Frame a = Simulate(spec, layout, 2.3, true).Frame;
                Frame b = Simulate(spec, layout, 2.3, true).Frame;
                bool det = SameFrame(a, b);
                if (!det) deterministicBad++;

                Rgb alt = new Rgb(40, 80, 255);
                Frame c = Simulate(spec, layout, 2.3, true, alt).Frame;
                bool uses = !SameFrame(a, c);
                bool ok = uses == spec.UsesColor;
                if (!ok) flagBad++;
                report.AppendLine(string.Format("{0,-12}  {1,-14} {2} (spec={3}, observed={4})",
                    spec.Id, det ? "ok" : "FAILED", ok ? "ok" : "MISMATCH",
                    spec.UsesColor, uses));
            }

            // ---- distinctness: no two effects may render the same frame -----
            report.AppendLine();
            List<string> signatures = new List<string>();
            Dictionary<string, string> sigOwner = new Dictionary<string, string>();
            int duplicateCount = 0;
            for (int si = 0; si < specs.Length; si++)
            {
                Frame f = Simulate(specs[si], layout, 2.3, true).Frame;
                string sig = Signature(f);
                if (sigOwner.ContainsKey(sig))
                {
                    duplicateCount++;
                    report.AppendLine("duplicate frame: " + specs[si].Id + " == " + sigOwner[sig]);
                }
                else sigOwner[sig] = specs[si].Id;
                signatures.Add(sig);
            }
            report.AppendLine("unique effect frames at t=2.3: " + sigOwner.Count + " / " + specs.Length);

            // ---- parameter sweep: every effect must survive the slider ends -----
            report.AppendLine();
            report.AppendLine("parameter sweep (speed 0.3 / 2.5, uniform mode, unknown id)");
            int sweepBad = 0;
            for (int si = 0; si < specs.Length; si++)
            {
                EffectSpec spec = specs[si];
                string err = "";
                try
                {
                    for (int k = 0; k < 2; k++)
                    {
                        double speed = k == 0 ? 0.3 : 2.5;
                        IEffect fx = EffectRegistry.Create(spec.Id, speed, BaseColor);
                        Frame fr = new Frame();
                        EffectContext ctx = MakeContext(true);
                        for (int step = 0; step < 8; step++)
                        {
                            double t = step * 0.37;
                            FillPositions(fr, layout, t);
                            fx.Render(fr, ctx);
                        }
                        // uniform: every LED at the desk middle
                        for (int i = 0; i < Frame.KeyCount; i++) fr.KeyX[i] = 0.5;
                        fr.LaptopX = 0.5;
                        fr.HeadsetX = 0.5;
                        fx.Render(fr, ctx);
                        if (!Finite(fr)) { err = "non-finite frame"; break; }
                    }
                }
                catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message; }
                if (err.Length > 0) sweepBad++;
                report.AppendLine(string.Format("{0,-12}  {1}", spec.Id, err.Length == 0 ? "ok" : ("FAILED " + err)));
            }
            IEffect unknown = EffectRegistry.Create("this-id-does-not-exist", 1.0, BaseColor);
            bool unknownOk = unknown != null && unknown.Id == EffectRegistry.DefaultId;
            if (!unknownOk) sweepBad++;
            report.AppendLine("unknown id falls back to '" + EffectRegistry.DefaultId + "': " + (unknownOk ? "ok" : "FAILED"));

            // ---- contact sheets ----------------------------------------------
            MakeContactSheet(Path.Combine(outDir, "contact_ported.png"), ported, thumbs);
            MakeContactSheet(Path.Combine(outDir, "contact_new.png"), added, thumbs);
            for (int si = 0; si < specs.Length; si++)
            {
                Bitmap[] arr = thumbs[specs[si].Id];
                for (int i = 0; i < arr.Length; i++) arr[i].Dispose();
            }

            report.AppendLine();
            report.AppendLine("panels: " + panelsWritten + " (+2 fallback), strips: " + specs.Length + ", contact sheets: 2");
            report.AppendLine("objective warnings: " + warnings);
            report.AppendLine("determinism failures: " + deterministicBad + ", UsesColor mismatches: " + flagBad
                + ", duplicate frames: " + duplicateCount + ", sweep failures: " + sweepBad);
            string verdict = (warnings == 0 && deterministicBad == 0 && flagBad == 0 && duplicateCount == 0
                && sweepBad == 0 && sigOwner.Count == specs.Length) ? "PASS" : "CHECK ABOVE";
            report.AppendLine("verdict: " + verdict);

            File.WriteAllText(Path.Combine(outDir, "fx_preview_report.txt"), report.ToString(), Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine(report.ToString());
            Console.WriteLine("[preview] written to " + outDir);
        }

        // =====================================================================
        // simulation
        // =====================================================================

        private static Sample Simulate(EffectSpec spec, KeyLayout layout, double targetT, bool services)
        {
            return Simulate(spec, layout, targetT, services, BaseColor);
        }

        /// <summary>
        /// Run the effect at exactly 30 fps from t=0 to targetT, feeding the same
        /// keystroke / audio / position inputs the app would, and return the final
        /// frame plus the average cost of one Render() call.
        /// </summary>
        private static Sample Simulate(EffectSpec spec, KeyLayout layout, double targetT, bool services, Rgb color)
        {
            IEffect fx = EffectRegistry.Create(spec.Id, 1.0, color);
            Frame f = new Frame();
            EffectContext ctx = new EffectContext();
            ctx.BaseColor = color;
            ctx.Keys = new KeyPulse();
            PreviewScreenHolder screen = null;
            PreviewAudioHolder audio = null;
            if (services)
            {
                screen = new PreviewScreenHolder();
                audio = new PreviewAudioHolder();
                ctx.Screen = screen.Sampler;
                ctx.Audio = audio.Meter;
            }

            int frames = (int)Math.Ceiling(targetT * Fps) + 1;
            int nextHit = 0;
            double renderMs = 0;
            for (int k = 0; k < frames; k++)
            {
                double t = k * Dt;
                while (nextHit < HitTimes.Length && t >= HitTimes[nextHit])
                {
                    ctx.Keys.Add(HitXs[nextHit], t);
                    nextHit++;
                }
                if (services) audio.Drive(t);

                f.T = t;
                for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = layout.GlobalX(i);
                f.LaptopX = 0.37;
                f.HeadsetX = 0.065;

                Stopwatch sw = Stopwatch.StartNew();
                fx.Render(f, ctx);
                sw.Stop();
                renderMs += sw.Elapsed.TotalMilliseconds;
            }

            Sample s = new Sample();
            s.Frame = f;
            s.T = f.T;
            s.MsPerFrame = frames > 0 ? renderMs / frames : 0;
            return s;
        }

        private static EffectContext MakeContext(bool services)
        {
            EffectContext ctx = new EffectContext();
            ctx.BaseColor = BaseColor;
            ctx.Keys = new KeyPulse();
            if (services)
            {
                ctx.Screen = new ScreenSampler();
                ctx.Audio = new AudioMeter();
            }
            return ctx;
        }

        private static void FillPositions(Frame f, KeyLayout layout, double t)
        {
            f.T = t;
            for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = layout.GlobalX(i);
            f.LaptopX = 0.37;
            f.HeadsetX = 0.065;
        }

        private static bool Finite(Frame f)
        {
            return !double.IsNaN(f.T) && !double.IsInfinity(f.T);
        }

        /// <summary>Thin holders so Simulate never leaks GDI/service objects.</summary>
        private sealed class PreviewScreenHolder
        {
            public readonly ScreenSampler Sampler = new ScreenSampler();
        }

        private sealed class PreviewAudioHolder
        {
            public readonly AudioMeter Meter = new AudioMeter();

            /// <summary>Deterministic music: 0.7 s beat, envelope + shimmer.</summary>
            public void Drive(double t)
            {
                double beat = t % 0.70;
                double env = Math.Exp(-beat * 4.0);
                Meter.Level = (float)Fx.Clamp01(0.20 + 0.55 * env + 0.08 * Math.Sin(t * 2.0));
                Meter.Bass = (float)Fx.Clamp01(0.30 + 0.60 * env);
                Meter.Mid = (float)Fx.Clamp01(0.25 + 0.35 * Math.Abs(Math.Sin(t * 1.7 + 1.0)));
                Meter.Treble = (float)Fx.Clamp01(0.18 + 0.50 * env * Math.Abs(Math.Sin(t * 5.0)));
                Meter.Beat = beat < 0.06;
            }
        }

        // =====================================================================
        // objective checks
        // =====================================================================

        private static double Lum(Rgb c)
        {
            return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        }

        private static void Stats(Frame f, out double avg, out double minL, out double maxL, out int black, out int distinct)
        {
            double sum = 0;
            minL = 1e9;
            maxL = -1e9;
            black = 0;
            List<int> seen = new List<int>();
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                Rgb c = f.Keyboard[i];
                double l = Lum(c);
                sum += l;
                if (l < minL) minL = l;
                if (l > maxL) maxL = l;
                if (l < 8.0) black++;
                int key = c.ToArgb();
                if (!seen.Contains(key)) seen.Add(key);
            }
            avg = sum / Frame.KeyCount;
            distinct = seen.Count;
        }

        private static bool SameFrame(Frame a, Frame b)
        {
            if (a.Laptop.R != b.Laptop.R || a.Laptop.G != b.Laptop.G || a.Laptop.B != b.Laptop.B) return false;
            if (a.Headset.R != b.Headset.R || a.Headset.G != b.Headset.G || a.Headset.B != b.Headset.B) return false;
            for (int i = 0; i < Frame.KeyCount; i++)
                if (a.Keyboard[i].R != b.Keyboard[i].R || a.Keyboard[i].G != b.Keyboard[i].G ||
                    a.Keyboard[i].B != b.Keyboard[i].B) return false;
            return true;
        }

        private static string Signature(Frame f)
        {
            StringBuilder sb = new StringBuilder(64);
            sb.Append(f.Headset.ToHex()).Append(f.Laptop.ToHex());
            for (int i = 0; i < Frame.KeyCount; i++) sb.Append(f.Keyboard[i].ToHex());
            return sb.ToString();
        }

        private static Rgb AvgKeys(Frame f)
        {
            long r = 0, g = 0, b = 0;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                r += f.Keyboard[i].R; g += f.Keyboard[i].G; b += f.Keyboard[i].B;
            }
            return new Rgb((byte)(r / Frame.KeyCount), (byte)(g / Frame.KeyCount), (byte)(b / Frame.KeyCount));
        }

        private static string Hex(Rgb c) { return c.ToHex(); }

        // =====================================================================
        // drawing
        // =====================================================================

        private static Color C(Rgb c) { return Color.FromArgb(255, c.R, c.G, c.B); }
        private static Color C(Rgb c, int alpha) { return Color.FromArgb(alpha, c.R, c.G, c.B); }
        private static Rgb Darken(Rgb c, double f)
        {
            return new Rgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        private static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        private static void FillRoundGlow(Graphics g, RectangleF r, float radius, Rgb color, int alpha, float inflate)
        {
            RectangleF gr = r;
            gr.Inflate(inflate, inflate);
            FillRound(g, gr, radius + inflate * 0.5f, C(color, alpha));
        }

        private static Bitmap DrawPanel(Sample s, EffectSpec spec, double t, KeyLayout layout)
        {
            Bitmap bmp = new Bitmap(PanelW, PanelH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    new Rectangle(0, 0, PanelW, PanelH),
                    Color.FromArgb(17, 20, 27), Color.FromArgb(4, 5, 8), 90f))
                    g.FillRectangle(bg, 0, 0, PanelW, PanelH);
                using (Pen p = new Pen(Color.FromArgb(20, 25, 34), 1f))
                    g.DrawLine(p, 20, PanelH - 12, PanelW - 20, PanelH - 12);

                using (SolidBrush white = new SolidBrush(Color.FromArgb(236, 242, 250)))
                    g.DrawString(spec.Name + "  ·  " + spec.Id + "  ·  t = " + t.ToString("0.00") + " s",
                                 TitleFont, white, 20f, 8f);
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(118, 130, 148)))
                    g.DrawString("Render " + s.MsPerFrame.ToString("0.000") + " ms/frame @30fps"
                                 + "    耳机 " + Hex(s.Frame.Headset)
                                 + "  笔记本 " + Hex(s.Frame.Laptop)
                                 + "  键盘均值 " + Hex(AvgKeys(s.Frame)),
                                 InfoFont, dim, 20f, 33f);

                DrawHeadset(g, s.Frame.Headset);
                DrawLaptop(g, s.Frame.Laptop);
                DrawKeyboard(g, s.Frame, layout);
            }
            return bmp;
        }

        private static void DrawHeadset(Graphics g, Rgb color)
        {
            RectangleF cup = new RectangleF(42f, 112f, 108f, 200f);
            FillRoundGlow(g, cup, 54f, color, 44, 14f);
            using (GraphicsPath p = RoundRect(cup, 54f))
            using (LinearGradientBrush b = new LinearGradientBrush(cup, C(color), C(Darken(color, 0.45)), 90f))
                g.FillPath(b, p);
            using (GraphicsPath p = RoundRect(cup, 54f))
            using (Pen pen = new Pen(C(color, 150), 1.4f))
                g.DrawPath(pen, p);

            // headband suggest
            using (Pen band = new Pen(Color.FromArgb(90, color.R, color.G, color.B), 5f))
            {
                band.StartCap = LineCap.Round;
                band.EndCap = LineCap.Round;
                g.DrawArc(band, 58f, 74f, 240f, 170f, 205f, 130f);
            }
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 134, 152)))
                g.DrawString("耳机  x=0.065  " + Hex(color), LabelFont, dim, 26f, 330f);
        }

        private static void DrawLaptop(Graphics g, Rgb color)
        {
            // screen: very dark wallpaper so the laptop reads as a device, not a hole
            RectangleF screen = new RectangleF(186f, 140f, 324f, 112f);
            using (GraphicsPath p = RoundRect(screen, 7f))
            using (LinearGradientBrush b = new LinearGradientBrush(screen,
                Color.FromArgb(255, 22, 32, 52), Color.FromArgb(255, 9, 12, 18), 90f))
                g.FillPath(b, p);
            using (SolidBrush w1 = new SolidBrush(Color.FromArgb(40, 70, 120, 190)))
                g.FillRectangle(w1, 198f, 152f, 120f, 46f);
            using (SolidBrush w2 = new SolidBrush(Color.FromArgb(30, 190, 120, 210)))
                g.FillRectangle(w2, 328f, 166f, 168f, 56f);
            using (GraphicsPath p = RoundRect(screen, 7f))
            using (Pen pen = new Pen(Color.FromArgb(255, 44, 51, 63), 1.6f))
                g.DrawPath(pen, p);

            // base + glowing keyboard deck
            RectangleF body = new RectangleF(180f, 252f, 336f, 58f);
            FillRoundGlow(g, body, 8f, color, 36, 12f);
            using (GraphicsPath p = RoundRect(body, 8f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 30, 35, 43)))
                g.FillPath(b, p);
            RectangleF deck = new RectangleF(200f, 262f, 296f, 38f);
            using (GraphicsPath p = RoundRect(deck, 5f))
            using (LinearGradientBrush b = new LinearGradientBrush(deck, C(color), C(Darken(color, 0.55)), 0f))
                g.FillPath(b, p);
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 134, 152)))
                g.DrawString("笔记本  x=0.37  " + Hex(color), LabelFont, dim, 258f, 330f);
        }

        private static void DrawKeyboard(Graphics g, Frame f, KeyLayout layout)
        {
            RectangleF box = new RectangleF(566f, 62f, 596f, 262f);
            FillRound(g, box, 12f, Color.FromArgb(255, 11, 14, 19));
            using (GraphicsPath p = RoundRect(box, 12f))
            using (Pen pen = new Pen(Color.FromArgb(255, 26, 31, 40), 1.4f))
                g.DrawPath(pen, p);

            // group key indices per row (keymap json may reorder them)
            List<int>[] rows = new List<int>[layout.RowCount];
            for (int r = 0; r < rows.Length; r++) rows[r] = new List<int>();
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                int r = layout.Row[i];
                if (r < 0) r = 0;
                if (r >= rows.Length) r = rows.Length - 1;
                rows[r].Add(i);
            }

            int padX = 12;
            float usable = box.Width - padX * 2;
            float gap = 3f;
            float rowPitch = 51f;
            float keyH = 44f;
            for (int r = 0; r < rows.Length; r++)
            {
                List<int> keys = rows[r];
                if (keys.Count == 0) continue;
                double units = 0;
                for (int k = 0; k < keys.Count; k++) units += layout.Width[keys[k]];
                if (units <= 0) units = keys.Count;
                float pxPerUnit = (usable - gap * (keys.Count - 1)) / (float)units;
                float x = box.X + padX;
                float y = box.Y + 10f + r * rowPitch;
                for (int k = 0; k < keys.Count; k++)
                {
                    int ki = keys[k];
                    float w = (float)(layout.Width[ki] * pxPerUnit);
                    if (w < 2f) w = 2f;
                    RectangleF kr = new RectangleF(x, y, w, keyH);
                    Rgb c = f.Keyboard[ki];

                    FillRoundGlow(g, kr, 4.5f, c, 42, 2.5f);
                    using (GraphicsPath p = RoundRect(kr, 4.5f))
                    using (LinearGradientBrush b = new LinearGradientBrush(
                        kr, C(c), C(Darken(c, 0.62)), 90f))
                        g.FillPath(b, p);
                    using (GraphicsPath p = RoundRect(kr, 4.5f))
                    using (Pen pen = new Pen(C(c, 165), 1f))
                        g.DrawPath(pen, p);
                    x += w + gap;
                }
            }
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 134, 152)))
                g.DrawString("键盘  x=0.60..1.00  ·  " + Frame.KeyCount + " 键", LabelFont, dim, 572f, 330f);
        }

        private static Bitmap ComposeStrip(Bitmap[] panels)
        {
            int gap = 8;
            Bitmap strip = new Bitmap(PanelW, PanelH * panels.Length + gap * (panels.Length - 1), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(strip))
            {
                g.Clear(Color.FromArgb(255, 3, 4, 6));
                for (int i = 0; i < panels.Length; i++)
                    g.DrawImageUnscaled(panels[i], 0, i * (PanelH + gap));
            }
            return strip;
        }

        private static Bitmap Thumbnail(Bitmap src, int w, int h)
        {
            Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return b;
        }

        private static void MakeContactSheet(string path, List<EffectSpec> specs, Dictionary<string, Bitmap[]> thumbs)
        {
            int thumbW = 360, thumbH = 110, pad = 4, labelW = 78, top = 8, left = 8;
            int cols = 3;
            int width = left + labelW + cols * (thumbW + pad);
            int height = top + specs.Count * (thumbH + pad);
            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(255, 6, 8, 11));
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    for (int r = 0; r < specs.Count; r++)
                    {
                        EffectSpec spec = specs[r];
                        Bitmap[] arr = thumbs[spec.Id];
                        float y = top + r * (thumbH + pad);
                        using (SolidBrush label = new SolidBrush(Color.FromArgb(210, 220, 232)))
                            g.DrawString(spec.Id, SheetFont, label, left + 4, y + thumbH * 0.42f);
                        for (int ti = 0; ti < arr.Length; ti++)
                        {
                            float x = left + labelW + ti * (thumbW + pad);
                            g.DrawImageUnscaled(arr[ti], (int)x, (int)y);
                        }
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
