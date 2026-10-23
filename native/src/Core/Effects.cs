// PrismSync native app - effect implementations (C# 5, .NET Framework 4.8).
//
// 18 effects: the ten ported from the Python prototype (rgbsync/core.py +
// rgbsync/effects.py parameters) and eight new ones.
//
// Conventions
// -----------
// * All effects are a function of the shared desk axis: x = 0 is the far left
//   (headset), x = 1 the far right (right end of the keyboard). The headset
//   sits at 0.065, the laptop at 0.37 and the keyboard covers 0.60..1.00, so
//   "spatial" effects really do flow across all three devices.
// * t is seconds since the effect (re)started. Each constructor receives the
//   final speed (base speed below * UI slider 0.3..2.5), converted once in
//   EffectRegistry.Create() - effects never read the slider themselves.
// * Global brightness is applied by the engine AFTER Render(); effects must not
//   dim everything again. Only intentional dips/floors are implemented here
//   (wave/comet tails, star sky, audio pulse, ...).
// * Per frame work must stay allocation free: every table is sized in the
//   constructor and Render() only touches value types.
using System;

namespace PrismSync
{
    /// <summary>Shares the Id/Name/Hint plumbing; effects only paint colours.</summary>
    internal abstract class EffectBase : SpatialEffect
    {
        private readonly EffectSpec _spec;
        protected EffectBase(EffectSpec spec) { _spec = spec; }
        public override string Id { get { return _spec.Id; } }
        public override string Name { get { return _spec.Name; } }
        public override string Hint { get { return _spec.Hint; } }

        /// <summary>Fractional part, also correct for negative input.</summary>
        protected static double Frac(double v) { return v - Math.Floor(v); }

        protected static byte ToByte(double v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)(v + 0.5);
        }

        /// <summary>a + b * f, per channel, saturated.</summary>
        protected static Rgb Add(Rgb a, Rgb b, double f)
        {
            return new Rgb(ToByte(a.R + b.R * f), ToByte(a.G + b.G * f),
                           ToByte(a.B + b.B * f));
        }

        /// <summary>Channel-wise maximum (keeps a floor under a dark sample).</summary>
        protected static Rgb AtLeast(Rgb c, Rgb floor)
        {
            return new Rgb(c.R > floor.R ? c.R : floor.R,
                           c.G > floor.G ? c.G : floor.G,
                           c.B > floor.B ? c.B : floor.B);
        }
    }

    // =====================================================================
    // Ported effects (parameters follow the Python registry in effects.py)
    // =====================================================================

    /// <summary>static / 常亮: one fixed colour everywhere (uses base colour).</summary>
    internal sealed class SolidEffect : EffectBase
    {
        private readonly Rgb _color;
        public SolidEffect(EffectSpec spec, Rgb color) : base(spec) { _color = color; }
        public override Rgb ColorAt(double x, double t) { return _color; }
    }

    /// <summary>
    /// breathe / 呼吸: identical cosine fade on every device (uses base colour).
    /// Python: period = max(0.6, 2.4 / speedMult), depth = 0.55.
    /// </summary>
    internal sealed class BreatheEffect : EffectBase
    {
        private readonly Rgb _color;
        private readonly double _period;
        private const double Depth = 0.55;

        public BreatheEffect(EffectSpec spec, Rgb color, double period) : base(spec)
        {
            _color = color;
            _period = period;
        }

        public override Rgb ColorAt(double x, double t)
        {
            double phase = 0.5 - 0.5 * Math.Cos(Fx.Tau * (t / _period));
            return _color.Scale(1.0 - Depth * (1.0 - phase));
        }
    }

    /// <summary>
    /// trio / 三连流: one rainbow band wrapped over the whole desk and sliding
    /// right. span 2.5 means the keyboard slice (0.60..1.00) carries one full
    /// hue cycle, so the headset, the laptop and the keyboard each show a
    /// different slice of the same band. Base speed 0.18 (Python effects.py).
    /// Single-zone devices sample the band at their own desk position.
    /// </summary>
    internal sealed class TrioEffect : EffectBase
    {
        private readonly double _speed;
        private const double Span = 2.5;

        public TrioEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            return Rgb.FromHsv(x * Span + t * _speed, 1.0, 1.0);
        }

        public override Rgb DeskColor(double t, double x) { return ColorAt(x, t); }
    }

    /// <summary>
    /// aurora / 极光: two sine waves at different frequencies push a narrow hue
    /// band around (base_hue 0.53, spread 0.2) while local brightness breathes.
    /// Deliberately limited hue range - a full rainbow gets tiring.
    /// Base speed 0.10 (Python).
    /// </summary>
    internal sealed class AuroraEffect : EffectBase
    {
        private readonly double _speed;
        private const double Sat = 0.82;
        private const double BaseHue = 0.53;
        private const double Spread = 0.2;

        public AuroraEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            double tt = t * _speed;
            double w1 = Math.Sin(x * 5.2 + tt * 9.0);
            double w2 = Math.Sin(x * 11.5 - tt * 6.0 + 1.7);
            double h = BaseHue + Spread * (0.66 * w1 + 0.34 * w2) + tt * 0.25;
            double v = 0.56 + 0.44 * (0.5 + 0.5 * Math.Sin(x * 7.0 - tt * 11.0 + 0.6));
            return Rgb.FromHsv(h, Sat, v);
        }

        public override Rgb DeskColor(double t, double x) { return ColorAt(x, t); }
    }

    /// <summary>
    /// duo / 双色流转: two fixed colours (cyan -> magenta) push each other across
    /// the desk with a smoothstep edge, 1.4 cycles across the axis.
    /// Base speed 0.14 (Python).
    /// </summary>
    internal sealed class DuoEffect : EffectBase
    {
        private static readonly Rgb C1 = new Rgb(0, 208, 255);
        private static readonly Rgb C2 = new Rgb(255, 0, 150);
        private readonly double _speed;
        private const double Cycles = 1.4;

        public DuoEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            double phase = (x * Cycles - t * _speed) * Fx.Tau;
            double f = 0.5 + 0.5 * Math.Sin(phase);
            f = f * f * (3.0 - 2.0 * f);          // smoothstep: softer band edges
            return Rgb.Lerp(C1, C2, f);
        }

        public override Rgb DeskColor(double t, double x) { return ColorAt(x, t); }
    }

    /// <summary>
    /// flow / 流光: scrolling hue gradient at full brightness (no dark area, so
    /// every device always looks on). Base speed 0.12 (Python).
    /// Single-zone devices show the full-brightness hue passing the middle of the
    /// desk (Python global_color), not the dark-ish edge of the gradient.
    /// </summary>
    internal sealed class FlowEffect : EffectBase
    {
        private readonly double _speed;
        private const double Span = 1.0;

        public FlowEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            return Rgb.FromHsv(x * Span + t * _speed, 1.0, 1.0);
        }

        public override Rgb DeskColor(double t, double x)
        {
            return Rgb.FromHsv(0.5 * Span + t * _speed, 1.0, 1.0);
        }
    }

    /// <summary>
    /// wave / 波浪: a narrow bright head sweeps the desk; everything else stays at
    /// a visible floor (0.32) so the laptop backlight never reads as switched off.
    /// Base speed 0.35, width 0.30, floor 0.32 (Python).
    /// Single-zone devices show the head hue at full brightness (global_color).
    /// </summary>
    internal sealed class WaveEffect : EffectBase
    {
        private readonly double _speed;
        private const double Width = 0.30;
        private const double Floor = 0.34;   // Python uses 0.32; 0.34 + sat 0.80
        private const double FloorSat = 0.80; // keeps a violet floor above "looks off"

        public WaveEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        private double Head(double t) { return Frac(t * _speed); }

        public override Rgb ColorAt(double x, double t)
        {
            double head = Head(t);
            double d = Fx.LoopDist(x, head);
            if (d > Width) return Rgb.FromHsv(head, FloorSat, Floor);
            double falloff = 1.0 - d / Width;
            return Rgb.FromHsv(head, 1.0, Floor + (1.0 - Floor) * falloff);
        }

        public override Rgb DeskColor(double t, double x)
        {
            return Rgb.FromHsv(Head(t), 1.0, 1.0);
        }
    }

    /// <summary>
    /// comet / 彗星: bright head with a long fading tail (tail 0.7), dark
    /// background floor 0.18. Base speed 0.30 (Python).
    /// Single-zone devices show the head hue at full brightness.
    /// </summary>
    internal sealed class CometEffect : EffectBase
    {
        private readonly double _speed;
        private const double Tail = 0.70;
        private const double Floor = 0.40;    // Python 0.18 reads as "off" on dark hues
        private const double FloorSat = 0.75;

        public CometEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        private double Head(double t) { return Frac(t * _speed); }

        public override Rgb ColorAt(double x, double t)
        {
            double head = Head(t);
            double behind = Frac(head - x);   // 0 at the head, grows into the tail
            if (behind > Tail) return Rgb.FromHsv(head, FloorSat, Floor);
            double fade = 1.0 - (behind / Tail) * (1.0 - Floor);
            return Rgb.FromHsv(head, 1.0, fade);
        }

        public override Rgb DeskColor(double t, double x)
        {
            return Rgb.FromHsv(Head(t), 1.0, 1.0);
        }
    }

    /// <summary>
    /// rainbow / 七彩同步: one hue for the whole desk, cycling in lockstep.
    /// Base speed 0.12 (Python).
    /// </summary>
    internal sealed class RainbowEffect : EffectBase
    {
        private readonly double _speed;
        public RainbowEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            return Rgb.FromHsv(t * _speed, 1.0, 1.0);
        }
    }

    /// <summary>
    /// hue / 色相渐变: same gradient shape everywhere, the hue also slowly turns.
    /// Base speed 0.20, span 0.5 (Python).
    /// </summary>
    internal sealed class HueSweepEffect : EffectBase
    {
        private readonly double _speed;
        private const double Span = 0.5;

        public HueSweepEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            return Rgb.FromHsv(t * _speed + x * Span, 1.0, 1.0);
        }
    }

    // =====================================================================
    // New effects
    // =====================================================================

    /// <summary>
    /// plasma / 星云: three octaves of value noise drift past each other, mapped
    /// to a violet/blue/teal hue band with a bright value floor, so it reads as
    /// slowly boiling gas rather than a dark pattern. Base speed 0.10.
    /// </summary>
    internal sealed class PlasmaEffect : EffectBase
    {
        private readonly double _speed;

        public PlasmaEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            double s = t * _speed;
            double u = x * 4.2;
            double n1 = Fx.Noise1(u + s * 0.55);
            double n2 = Fx.Noise1(u * 2.3 - s * 0.90 + 3.1);
            double n3 = Fx.Noise1(u * 4.9 + s * 1.70 + 11.7);
            double n = 0.55 * n1 + 0.30 * n2 + 0.15 * n3;      // 0..1 cloud density
            double h = 0.60 + 0.55 * (n - 0.5) + 0.06 * (n3 - 0.5)
                       + 0.03 * Math.Sin(s * 0.6 + x * 2.0);
            double v = 0.55 + 0.45 * n;                        // never darker than 0.55
            double sat = 0.78 - 0.20 * n;                      // hot cores whiten a bit
            return Rgb.FromHsv(h, sat, v);
        }
    }

    /// <summary>
    /// stars / 繁星点点: a dark blue sky with 40 deterministic stars that slowly
    /// drift and twinkle. Stars are hash-seeded (never random per frame) and the
    /// gaussian falloff lights one or two keys at a time on the keyboard.
    /// Base speed 0.16 (drift + twinkle).
    /// Single-zone devices get a bright blue-white "headline" colour instead of
    /// sampling the dark sky (see DeskColor).
    /// </summary>
    internal sealed class StarsEffect : EffectBase
    {
        private const int StarCount = 56;
        private readonly double _speed;
        private readonly double[] _start;    // initial position, turns
        private readonly double[] _drift;    // turns / second at speed 1
        private readonly double[] _twinkle;  // twinkle cycles / second
        private readonly double[] _phase;    // twinkle phase 0..1
        private readonly double[] _size;     // gaussian width, desk units
        private readonly byte[] _cr;         // precomputed star colour
        private readonly byte[] _cg;
        private readonly byte[] _cb;
        private readonly Rgb _sky;

        public StarsEffect(EffectSpec spec, double speed) : base(spec)
        {
            _speed = speed * 10.0;   // turns/sec * speed (0.16 -> slow drift)
            _start = new double[StarCount];
            _drift = new double[StarCount];
            _twinkle = new double[StarCount];
            _phase = new double[StarCount];
            _size = new double[StarCount];
            _cr = new byte[StarCount];
            _cg = new byte[StarCount];
            _cb = new byte[StarCount];
            _sky = Rgb.FromHsv(0.64, 0.82, 0.13);

            for (int i = 0; i < StarCount; i++)
            {
                _start[i] = Fx.Hash(i * 7 + 1, 3);
                _drift[i] = (Fx.Hash(i * 7 + 2, 7) - 0.5) * 0.0040;
                _twinkle[i] = 0.10 + 0.55 * Fx.Hash(i * 7 + 3, 11);
                _phase[i] = Fx.Hash(i * 7 + 4, 13);
                // 0.0025..0.0055 desk units ~= one to two keyboard keys wide
                _size[i] = 0.0025 + 0.0030 * Fx.Hash(i * 7 + 5, 19);

                double pick = Fx.Hash(i * 7 + 6, 23);
                double hue;
                double sat;
                if (pick < 0.62) { hue = 0.58 + 0.04 * Fx.Hash(i, 29); sat = 0.22; } // white-blue
                else if (pick < 0.86) { hue = 0.60 + 0.05 * Fx.Hash(i, 31); sat = 0.70; } // blue
                else { hue = 0.09 + 0.03 * Fx.Hash(i, 37); sat = 0.60; }                 // warm
                Rgb c = Rgb.FromHsv(hue, sat, 1.0);
                _cr[i] = c.R; _cg[i] = c.G; _cb[i] = c.B;
            }
        }

        public override Rgb ColorAt(double x, double t)
        {
            double r = _sky.R, g = _sky.G, b = _sky.B;
            for (int i = 0; i < StarCount; i++)
            {
                double dx = Fx.LoopDist(x, Frac(_start[i] + t * _drift[i] * _speed));
                double w = _size[i];
                if (dx > w * 2.2) continue;
                double tw = 0.5 + 0.5 * Math.Sin((t * _twinkle[i] + _phase[i]) * Fx.Tau);
                tw = tw * tw * tw;                       // short, sharp glints
                double fall = Math.Exp(-(dx * dx) / (w * w));
                double amp = fall * (0.18 + 0.82 * tw);
                r += _cr[i] * amp;
                g += _cg[i] * amp;
                b += _cb[i] * amp;
            }
            return new Rgb(ToByte(r), ToByte(g), ToByte(b));
        }

        public override Rgb DeskColor(double t, double x)
        {
            // dark-sky effect: a single-zone device must still look lit
            return Rgb.FromHsv(0.58, 0.30, 0.90 + 0.10 * Fx.Sin01(t * 0.11));
        }
    }

    /// <summary>
    /// meteor / 流星雨: four meteors with different hues, speeds and tail lengths
    /// cross the desk at the same time, each with a white-hot head.
    /// Base speed 0.22.
    /// Single-zone devices show a bright "headline" colour (not the dark sky).
    /// </summary>
    internal sealed class MeteorEffect : EffectBase
    {
        private const int MeteorCount = 4;
        private readonly double _speed;
        private readonly double[] _start;
        private readonly double[] _rate;   // speed multiplier per meteor
        private readonly double[] _len;    // tail length, desk units
        private readonly byte[] _cr;
        private readonly byte[] _cg;
        private readonly byte[] _cb;

        public MeteorEffect(EffectSpec spec, double speed) : base(spec)
        {
            _speed = speed;
            _start = new double[MeteorCount];
            _rate = new double[MeteorCount];
            _len = new double[MeteorCount];
            _cr = new byte[MeteorCount];
            _cg = new byte[MeteorCount];
            _cb = new byte[MeteorCount];

            double[] hues = new double[] { 0.53, 0.78, 0.95, 0.11 };
            for (int i = 0; i < MeteorCount; i++)
            {
                _start[i] = Fx.Hash(i * 13 + 5, 41);
                _rate[i] = 0.80 + 0.45 * Fx.Hash(i * 13 + 6, 43);
                _len[i] = 0.14 + 0.10 * Fx.Hash(i * 13 + 7, 47);
                Rgb c = Rgb.FromHsv(hues[i] + 0.03 * Fx.Hash(i, 53), 0.80, 1.0);
                _cr[i] = c.R; _cg[i] = c.G; _cb[i] = c.B;
            }
        }

        public override Rgb ColorAt(double x, double t)
        {
            // Night sky: slightly uneven blue + a few twinkling dust motes, so the
            // board never reads as switched off between meteors.
            double skyV = 0.17 + 0.08 * Fx.Sin01(x * 2.3 + t * 0.03);
            Rgb sky = Rgb.FromHsv(0.62, 0.78, skyV);
            double r = sky.R, g = sky.G, b = sky.B;
            int cell = (int)Math.Floor(x * 60.0);
            double dust = Fx.Hash(cell, (int)Math.Floor(t * 1.5) + 3);
            if (dust > 0.86)
            {
                double amp = (dust - 0.86) / 0.14 * 0.45;
                r += 170.0 * amp; g += 190.0 * amp; b += 235.0 * amp;
            }

            for (int i = 0; i < MeteorCount; i++)
            {
                double head = Frac(_start[i] + t * _speed * _rate[i]);
                double behind = Frac(head - x);        // 0 at head, grows behind it
                if (behind > _len[i]) continue;
                double f = 1.0 - behind / _len[i];     // 1 at head -> 0 at tail end
                double f2 = f * f;
                double amp = 0.20 * f + 0.80 * f2 * f; // long dim tail, bright core
                r += _cr[i] * amp;
                g += _cg[i] * amp;
                b += _cb[i] * amp;
                if (behind < 0.016)                    // white-hot head
                {
                    double white = (1.0 - behind / 0.016) * 0.85;
                    r += 255.0 * white;
                    g += 255.0 * white;
                    b += 255.0 * white;
                }
            }
            return new Rgb(ToByte(r), ToByte(g), ToByte(b));
        }

        public override Rgb DeskColor(double t, double x)
        {
            // bright cyan-white headline with a slow shimmer
            return Rgb.FromHsv(0.59 + 0.03 * Math.Sin(t * 0.7), 0.55, 1.0);
        }
    }

    /// <summary>
    /// fire / 焰火: layered noise + fast flicker mapped to a red-orange-yellow
    /// ramp (hot cores desaturate towards white), with occasional ember pops.
    /// Base speed 0.35.
    /// </summary>
    internal sealed class FireEffect : EffectBase
    {
        private readonly double _speed;

        public FireEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            double s = t * _speed;
            double n1 = Fx.Noise1(x * 7.0 + s * 2.6);
            double n2 = Fx.Noise1(x * 13.0 - s * 4.1 + 7.3);
            double n3 = Fx.Noise1(x * 3.0 + s * 1.3 + 21.1);
            double heat = 0.45 * n1 + 0.30 * n2 + 0.25 * n3;
            heat = heat * heat * (3.0 - 2.0 * heat);
            double flick = 0.5 + 0.5 * Math.Sin(s * 9.0 + x * 23.0);
            heat = Fx.Clamp01(heat * 0.85 + 0.15 * flick);

            double hue = 0.015 + 0.115 * heat;                 // red -> orange -> yellow
            double hot = Fx.Clamp01((heat - 0.72) / 0.28);
            double sat = 1.0 - 0.55 * hot;                     // white-hot cores
            double val = 0.30 + 0.70 * heat;
            Rgb c = Rgb.FromHsv(hue, sat, val);

            // Sparse popping embers: a hash cell changes a few times per second.
            int cell = (int)Math.Floor(x * 90.0);
            double spark = Fx.Hash(cell, (int)Math.Floor(s * 6.0));
            if (spark > 0.935)
            {
                double f = (spark - 0.935) / 0.065;
                c = Add(c, new Rgb(255, 224, 150), f * 0.55);
            }
            return c;
        }

        public override Rgb DeskColor(double t, double x)
        {
            return Rgb.FromHsv(0.075 + 0.012 * Fx.Sin01(t * 3.7), 0.92, 1.0);
        }
    }

    /// <summary>
    /// ripple / 涟漪: three rings leave the middle of the desk and expand
    /// outwards (radius 0 -> 0.5 desk units), fading as they reach the edge.
    /// Base speed 0.40.
    /// Single-zone devices show a bright ring colour instead of the dark gaps.
    /// </summary>
    internal sealed class RippleEffect : EffectBase
    {
        private const int Rings = 3;
        private const double RingWidth = 0.026;
        private static readonly Rgb Deep = new Rgb(12, 20, 48);
        private readonly double _speed;

        public RippleEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        public override Rgb ColorAt(double x, double t)
        {
            double s = t * _speed;
            double d = Math.Abs(x - 0.5);

            // base: centre glow plus a faint blue ambient so the board never
            // goes fully black between rings
            double centre = Math.Exp(-(d * d) / (0.040 * 0.040));
            Rgb c = Add(Deep, Rgb.FromHsv(0.52, 0.9, 1.0), 0.34 * centre);
            c = Add(c, Rgb.FromHsv(0.60, 0.9, 1.0), 0.08);

            for (int k = 0; k < Rings; k++)
            {
                double radius = Frac(s * 0.50 + k / (double)Rings) * 0.5;
                double band = (d - radius) / RingWidth;
                double amp = Math.Exp(-band * band) * (1.0 - radius / 0.5);
                if (amp < 0.02) continue;
                double hue = 0.52 + 0.11 * k + 0.04 * Math.Sin(s * 0.5);
                Rgb ring = Rgb.FromHsv(hue, 0.85, 1.0);
                c = Add(c, ring, amp * 1.5);
                // soft halo around the ring keeps the gap from looking dead
                c = Add(c, ring, amp * 0.30);
                if (amp > 0.80) c = Add(c, Rgb.White, (amp - 0.80) * 1.6);
            }
            return c;
        }

        public override Rgb DeskColor(double t, double x)
        {
            return Rgb.FromHsv(0.53 + 0.08 * Fx.Sin01(t * 0.13), 0.70, 1.0);
        }
    }

    /// <summary>
    /// keyhit / 击键涟漪: every keystroke sends a light ring out from that key
    /// (KeyPulse.EnergyAt), over a dim teal idle background. Falls back to the
    /// idle glow when no keystrokes were seen. Base expansion speed 0.55.
    /// Uses none of the base colour.
    /// </summary>
    internal sealed class KeyHitEffect : EffectBase
    {
        private readonly double _speed;
        private const double Life = 2.6;
        private const double IdleHue = 0.55;
        private static readonly Rgb IdleFloor = new Rgb(4, 20, 26);

        public KeyHitEffect(EffectSpec spec, double speed) : base(spec) { _speed = speed; }

        private Rgb IdleColor(double x, double t)
        {
            double breathe = 0.14 + 0.06 * Fx.Sin01(t * 0.17 + x * 0.8);
            return Add(IdleFloor, Rgb.FromHsv(IdleHue + 0.02 * Math.Sin(t * 0.4 + x), 0.78, 1.0), breathe);
        }

        private Rgb HotColor(double energy)
        {
            // ring colour: white-hot cyan core, cooling to blue on the outside
            return Rgb.FromHsv(0.50 + 0.06 * energy, 1.0 - 0.78 * energy, 1.0);
        }

        public override void Render(Frame f, EffectContext ctx)
        {
            double t = f.T;
            KeyPulse keys = ctx != null ? ctx.Keys : null;
            for (int i = 0; i < Frame.KeyCount; i++)
            {
                double x = f.KeyX[i];
                Rgb idle = IdleColor(x, t);
                double e = keys != null ? keys.EnergyAt(x, t, Life, _speed) : 0.0;
                f.Keyboard[i] = e > 0.005 ? Add(idle, HotColor(e), e) : idle;
            }

            // single-zone devices: bright idle colour, still reacting to nearby hits
            double lh = keys != null ? keys.EnergyAt(f.LaptopX, t, Life, _speed) : 0.0;
            double hh = keys != null ? keys.EnergyAt(f.HeadsetX, t, Life, _speed) : 0.0;
            Rgb laptopIdle = Rgb.FromHsv(IdleHue, 0.62, 0.86);
            Rgb headIdle = Rgb.FromHsv(IdleHue, 0.62, 0.86);
            f.Laptop = Add(laptopIdle, HotColor(lh), lh);
            f.Headset = Add(headIdle, HotColor(hh), hh);
        }

        public override Rgb ColorAt(double x, double t)
        {
            // not used (Render is overridden) but keeps the base class honest
            return IdleColor(x, t);
        }
    }

    /// <summary>
    /// screen / 屏幕氛围: samples the 32x18 downscaled screen (ScreenSampler).
    /// One Grab per 0.12 s, then a per-frame exponential smoothing (~0.15 s time
    /// constant) so fast video does not strobe. The keyboard maps 0.60..1.00 of
    /// the desk axis onto the full screen width; the single-zone devices get the
    /// screen average. When no screen is available the effect degrades to a soft
    /// flowing teal/violet gradient instead of going dark.
    /// </summary>
    internal sealed class ScreenAmbientEffect : EffectBase
    {
        private const int Cols = ScreenSampler.Cols;
        private const double GrabInterval = 0.12;
        private const double SmoothRate = 9.0;
        private static readonly Rgb FloorTint = new Rgb(6, 12, 22);

        private readonly Rgb[] _cols = new Rgb[Cols];
        private readonly Rgb[] _target = new Rgb[Cols];
        private double _lastGrab = -1e9;
        private double _lastT = -1;
        private bool _live;
        private Rgb _avg;
        private Rgb _headline;

        public ScreenAmbientEffect(EffectSpec spec) : base(spec)
        {
            for (int i = 0; i < Cols; i++) { _cols[i] = Rgb.Black; _target[i] = Rgb.Black; }
        }

        private double Dt(double t)
        {
            double dt = _lastT < 0 ? 1.0 / 30.0 : t - _lastT;
            if (dt < 0.001) dt = 0.001;
            if (dt > 0.20) dt = 0.20;
            _lastT = t;
            return dt;
        }

        private static Rgb SampleColumn(ScreenSampler s, int col)
        {
            // average a few rows through the middle of the screen for stability
            Rgb a = s.Cell(col, 6);
            Rgb b = s.Cell(col, 9);
            Rgb c = s.Cell(col, 12);
            return new Rgb((byte)((a.R + b.R + c.R) / 3),
                           (byte)((a.G + b.G + c.G) / 3),
                           (byte)((a.B + b.B + c.B) / 3));
        }

        /// <summary>
        /// Screen colours are shown a touch more saturated and lifted slightly, so
        /// a dim desktop still glows; bright content is NOT amplified (no clipping
        /// to white), which is why there is no fixed gain here.
        /// </summary>
        private static Rgb AmbientColor(Rgb c)
        {
            int mn = Math.Min(c.R, Math.Min(c.G, c.B));
            return AtLeast(new Rgb(ToByte(c.R + (c.R - mn) * 0.45 + 10.0),
                                   ToByte(c.G + (c.G - mn) * 0.45 + 10.0),
                                   ToByte(c.B + (c.B - mn) * 0.45 + 10.0)),
                           FloorTint);
        }

        /// <summary>
        /// Average of the brighter columns only. A plain screen average is dragged
        /// dark by wallpapers / window chrome, which makes a single-zone device look
        /// half off; the bright content is what the ambient colour should show.
        /// </summary>
        private Rgb HeadlineFromTargets()
        {
            double maxL = 1.0;
            for (int c = 0; c < Cols; c++)
            {
                double l = 0.2126 * _target[c].R + 0.7152 * _target[c].G + 0.0722 * _target[c].B;
                if (l > maxL) maxL = l;
            }
            double cut = maxL * 0.55;
            long r = 0, g = 0, b = 0;
            int n = 0;
            for (int c = 0; c < Cols; c++)
            {
                Rgb t = _target[c];
                double l = 0.2126 * t.R + 0.7152 * t.G + 0.0722 * t.B;
                if (l < cut) continue;
                r += t.R; g += t.G; b += t.B; n++;
            }
            if (n == 0) return _avg;
            return new Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }

        public override void Render(Frame f, EffectContext ctx)
        {
            double t = f.T;
            double dt = Dt(t);
            ScreenSampler sc = ctx != null ? ctx.Screen : null;

            if (sc != null && (t - _lastGrab >= GrabInterval || _lastGrab < -1e8))
            {
                _lastGrab = t;
                bool ok = false;
                try { ok = sc.Grab(); }
                catch { ok = false; }
                if (ok)
                {
                    _live = true;
                    for (int c = 0; c < Cols; c++) _target[c] = SampleColumn(sc, c);
                    _avg = sc.Average();
                    _headline = HeadlineFromTargets();
                }
                else if (!sc.Available)
                {
                    _live = false;
                }
            }

            if (_live && sc != null)
            {
                double k = 1.0 - Math.Exp(-dt * SmoothRate);
                for (int c = 0; c < Cols; c++)
                {
                    _cols[c] = Rgb.Lerp(_cols[c], _target[c], k);
                    _cols[c] = AmbientColor(_cols[c]);
                }
                for (int i = 0; i < Frame.KeyCount; i++)
                {
                    double u = Fx.Clamp01((f.KeyX[i] - 0.60) / 0.40);
                    int ci = (int)(u * (Cols - 1) + 0.5);
                    if (ci < 0) ci = 0; else if (ci > Cols - 1) ci = Cols - 1;
                    f.Keyboard[i] = _cols[ci];
                }
                Rgb avg = AmbientColor(_headline);
                f.Laptop = avg;
                f.Headset = avg;
                return;
            }

            // --- graceful fallback: soft flowing gradient, still synced ---------
            for (int i = 0; i < Frame.KeyCount; i++)
                f.Keyboard[i] = FallbackColor(f.KeyX[i], t);
            f.Laptop = FallbackColor(f.LaptopX, t);
            f.Headset = FallbackColor(f.HeadsetX, t);
        }

        private static Rgb FallbackColor(double x, double t)
        {
            double h = 0.55 + 0.10 * Math.Sin(t * 0.25) + 0.10 * x;
            double v = 0.50 + 0.28 * Fx.Sin01(t * 0.13 + x * 0.5);
            return Rgb.FromHsv(h, 0.72, v);
        }

        public override Rgb ColorAt(double x, double t)
        {
            return FallbackColor(x, t);
        }
    }

    /// <summary>
    /// audio / 音频律动: mic level drives brightness, bass the hue, mid/treble the
    /// local shimmer; a transient (level rise or Beat) fires a pulse that decays
    /// with a ~0.3 s time constant. Base speed 1.0 (only used for the decay and
    /// hue rotation, not for a travelling pattern).
    /// With no microphone it degrades to a cyan breathing glow.
    /// </summary>
    internal sealed class AudioPulseEffect : EffectBase
    {
        private readonly double _speed;
        private double _lastT = -1;
        private double _level;
        private double _pulse;
        private double _hueBase;

        public AudioPulseEffect(EffectSpec spec, double speed) : base(spec)
        {
            _speed = speed;
        }

        public override void Render(Frame f, EffectContext ctx)
        {
            double t = f.T;
            double dt = _lastT < 0 ? 1.0 / 30.0 : t - _lastT;
            if (dt < 0.001) dt = 0.001;
            if (dt > 0.20) dt = 0.20;
            _lastT = t;

            AudioMeter a = ctx != null ? ctx.Audio : null;
            bool live = a != null && a.Available;
            if (!live)
            {
                // graceful fallback: calm cyan breathe (no base colour involved)
                for (int i = 0; i < Frame.KeyCount; i++)
                    f.Keyboard[i] = Fallback(f.KeyX[i], t);
                f.Laptop = Fallback(f.LaptopX, t);
                f.Headset = Fallback(f.HeadsetX, t);
                return;
            }

            double lvl = Fx.Clamp01(a.Level);
            double bass = Fx.Clamp01(a.Bass);
            double mid = Fx.Clamp01(a.Mid);
            double treble = Fx.Clamp01(a.Treble);

            bool transient = a.Beat || lvl > _level + 0.06;
            if (transient) _pulse = 1.0;
            _level += (lvl - _level) * Fx.Clamp01(dt * 14.0);   // smoothed level
            _pulse *= Math.Exp(-dt * 2.4);                      // ~0.3 s pulse tail

            // cyan by default; bass pushes towards violet, treble adds sparkle
            _hueBase = 0.44 + 0.16 * bass + 0.10 * mid + 0.08 * treble
                       + 0.04 * Math.Sin(t * 0.12 * _speed);
            double energy = 0.25 + 0.75 * Math.Max(_pulse, _level * 0.85);
            double v = 0.36 + 0.64 * energy;

            for (int i = 0; i < Frame.KeyCount; i++)
            {
                double x = f.KeyX[i];
                double shimmer = 1.0 + 0.16 * treble * Math.Sin(t * 3.0 + x * 4.0);
                double band = 1.0 + 0.10 * bass * Fx.Sin01(t * 0.8 + x * 2.0);
                double hue = _hueBase + 0.06 * x;
                f.Keyboard[i] = Rgb.FromHsv(hue, 0.72, Fx.Clamp01(v * shimmer * band));
            }
            // single-zone devices: same energy, never below a visible glow
            double dv = Math.Max(v, 0.55);
            f.Laptop = Rgb.FromHsv(_hueBase + 0.06 * f.LaptopX, 0.68, Fx.Clamp01(dv));
            f.Headset = Rgb.FromHsv(_hueBase + 0.06 * f.HeadsetX, 0.68, Fx.Clamp01(dv));
        }

        private static Rgb Fallback(double x, double t)
        {
            double phase = 0.5 - 0.5 * Math.Cos(Fx.Tau * (t / 2.0));
            double v = 0.45 + 0.40 * phase;
            return Rgb.FromHsv(0.55 + 0.05 * x + 0.03 * Math.Sin(t * 0.2), 0.70, v);
        }

        public override Rgb ColorAt(double x, double t)
        {
            return Fallback(x, t);
        }
    }
}
