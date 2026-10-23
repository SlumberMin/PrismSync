// PrismSync native app - effect contract shared by the effect library and the engine.
using System;
using System.Collections.Generic;

namespace PrismSync
{
    /// <summary>
    /// Everything an effect may read besides time and position.
    /// Services can be null when unavailable (no mic, no screen access, ...).
    /// </summary>
    public sealed class EffectContext
    {
        public Rgb BaseColor = new Rgb(255, 0, 0);
        /// <summary>Global dimmer, applied by the engine after rendering (0..1).</summary>
        public double Brightness = 1.0;
        /// <summary>Keyboard activity (raw-input keystrokes), never null.</summary>
        public KeyPulse Keys = new KeyPulse();
        /// <summary>Screen colour sampler, may be null.</summary>
        public ScreenSampler Screen;
        /// <summary>Microphone level / band meter, may be null.</summary>
        public AudioMeter Audio;
        /// <summary>True when the user asked every device to show one colour.</summary>
        public bool Uniform;
    }

    /// <summary>
    /// One lighting effect. Render() must fill every field of the frame.
    /// Effects are cheap to construct and are rebuilt when a slider moves, so
    /// never do expensive setup here.
    /// </summary>
    public interface IEffect
    {
        string Id { get; }
        string Name { get; }
        string Hint { get; }
        void Render(Frame frame, EffectContext ctx);
    }

    /// <summary>
    /// Base for the (majority) effects that are a pure function of desk position
    /// and time: implement ColorAt(), and the base fills the keyboard plus the two
    /// single-zone devices.
    /// </summary>
    public abstract class SpatialEffect : IEffect
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Hint { get; }

        /// <summary>Desk-axis colour: x is 0 (far left) .. 1 (far right).</summary>
        public abstract Rgb ColorAt(double x, double t);

        /// <summary>
        /// Colour for a single-zone device (laptop / headset). Effects with a dark
        /// travelling head override this so the device does not sit in the dark
        /// part of the wave and read as "switched off".
        /// </summary>
        public virtual Rgb DeskColor(double t, double x) { return ColorAt(x, t); }

        public virtual void Render(Frame f, EffectContext ctx)
        {
            double t = f.T;
            for (int i = 0; i < Frame.KeyCount; i++) f.Keyboard[i] = ColorAt(f.KeyX[i], t);
            f.Laptop = DeskColor(t, f.LaptopX);
            f.Headset = DeskColor(t, f.HeadsetX);
        }
    }

    /// <summary>One recent keystroke: where on the keyboard it happened, and when.</summary>
    public struct KeyHit
    {
        public double X;
        public double T;
    }

    /// <summary>Rolling list of keystrokes, used by key-reactive effects.</summary>
    public sealed class KeyPulse
    {
        public const int MaxHits = 32;
        private readonly List<KeyHit> _hits = new List<KeyHit>();
        public double TotalCount;

        public void Add(double x, double t)
        {
            _hits.Add(new KeyHit { X = Fx.Clamp01(x), T = t });
            if (_hits.Count > MaxHits) _hits.RemoveAt(0);
            TotalCount += 1.0;
        }

        public void Decay(double t, double lifeSeconds)
        {
            while (_hits.Count > 0 && t - _hits[0].T > lifeSeconds) _hits.RemoveAt(0);
        }

        public void Clear() { _hits.Clear(); }

        public int Count { get { return _hits.Count; } }

        public KeyHit this[int i] { get { return _hits[i]; } }

        public double EnergyAt(double x, double t, double life, double speed)
        {
            double sum = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                double age = t - _hits[i].T;
                if (age < 0 || age > life) continue;
                double d = Math.Abs(x - _hits[i].X);
                double radius = age * speed;
                double band = Math.Abs(d - radius);
                sum += (1.0 - age / life) * Math.Exp(-band * 18.0);
            }
            return Math.Min(1.0, sum);
        }
    }
}
