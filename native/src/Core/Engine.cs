// PrismSync native app - one clock, one effect, every device.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PrismSync
{
    public sealed class EngineStats
    {
        public double Fps;
        public long Frames;
        public double Uptime;
    }

    /// <summary>
    /// Runs the effect on a fixed cadence and pushes each frame to every open
    /// device. Effect swaps and device hot-plug are safe while it is running.
    /// </summary>
    public sealed class Engine : IDisposable
    {
        private readonly object _effectLock = new object();
        private readonly object _deviceLock = new object();
        private readonly object _previewLock = new object();

        private readonly List<IDeviceAdapter> _devices = new List<IDeviceAdapter>();
        private IEffect _effect;
        private EffectContext _ctx = new EffectContext();
        private Frame _frame = new Frame();
        private Frame _idleFrame = new Frame();
        private PreviewData _preview = new PreviewData();
        private KeyLayout _layout = new KeyLayout();

        private Thread _thread;
        private volatile bool _stop;
        private volatile bool _running;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _t0;
        private double _period = 1.0 / 30.0;
        private double _next;
        private long _frames;
        private double _fpsMark;
        private double _fps;
        private double _startedAt;

        public double Speed = 1.0;
        public double Brightness = 1.0;
        public bool Uniform;
        public int TargetFps = 30;
        public bool CloseToTray = true;

        public event Action<string> Log;

        public Engine()
        {
            _ctx.Keys = new KeyPulse();
        }

        public KeyLayout Layout { get { return _layout; } }
        public KeyPulse Keys { get { return _ctx.Keys; } }
        public EffectContext Context { get { return _ctx; } }
        public bool IsRunning { get { return _running; } }
        public IEffect CurrentEffect { get { lock (_effectLock) { return _effect; } } }

        public void AttachScreen(ScreenSampler s) { _ctx.Screen = s; }
        public void AttachAudio(AudioMeter a) { _ctx.Audio = a; }

        public void SetTargetFps(int fps)
        {
            if (fps < 10) fps = 10;
            if (fps > 120) fps = 120;
            TargetFps = fps;
            _period = 1.0 / fps;
        }

        public void SetEffect(IEffect effect, bool resetPhase)
        {
            lock (_effectLock)
            {
                _effect = effect;
                if (resetPhase)
                {
                    _t0 = _clock.Elapsed.TotalSeconds;
                    _ctx.Keys.Clear();
                }
            }
        }

        public void AddDevice(IDeviceAdapter dev)
        {
            if (dev == null) return;
            lock (_deviceLock)
            {
                for (int i = 0; i < _devices.Count; i++)
                    if (_devices[i].Id == dev.Id) return;
                _devices.Add(dev);
            }
        }

        public void RemoveDevice(IDeviceAdapter dev)
        {
            if (dev == null) return;
            lock (_deviceLock) { _devices.Remove(dev); }
            try { dev.Close(); } catch { }
        }

        public void ClearDevices()
        {
            List<IDeviceAdapter> snapshot;
            lock (_deviceLock)
            {
                snapshot = new List<IDeviceAdapter>(_devices);
                _devices.Clear();
            }
            for (int i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i].Close(); } catch { }
            }
        }

        public IDeviceAdapter[] Devices()
        {
            lock (_deviceLock) { return _devices.ToArray(); }
        }

        public EngineStats Stats()
        {
            EngineStats s = new EngineStats();
            s.Fps = _fps;
            s.Frames = Interlocked.Read(ref _frames);
            s.Uptime = _running ? (_clock.Elapsed.TotalSeconds - _startedAt) : 0;
            return s;
        }

        public void Start()
        {
            if (_running) return;
            _stop = false;
            _running = true;
            _startedAt = _clock.Elapsed.TotalSeconds;
            _t0 = _startedAt;
            _next = _startedAt;
            _frames = 0;
            _fpsMark = _startedAt;
            _fps = 0;
            int fps = TargetFps;
            _period = 1.0 / Math.Max(10, fps);
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "rgb-engine";
            _thread.Priority = ThreadPriority.AboveNormal;
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _stop = true;
            Thread th = _thread;
            _thread = null;
            if (th != null) th.Join(2000);
            _running = false;
            _fps = 0;
        }

        private void Loop()
        {
            double next = _next;
            long frames = 0;
            double fpsMark = _fpsMark;
            while (!_stop)
            {
                double now = _clock.Elapsed.TotalSeconds;
                if (now < next - 0.0005)
                {
                    int ms = (int)((next - now) * 1000.0);
                    if (ms > 0) Thread.Sleep(ms);
                    now = _clock.Elapsed.TotalSeconds;
                }
                next += _period;
                if (next < now - 0.25) next = now + _period;

                double t = now - _t0;
                Frame f = _frame;
                f.T = t;

                if (Uniform)
                {
                    for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = 0.5;
                    f.LaptopX = 0.5;
                    f.HeadsetX = 0.5;
                }
                else
                {
                    for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = _layout.GlobalX(i);
                    f.LaptopX = 0.37;
                    f.HeadsetX = 0.065;
                }

                IEffect effect;
                lock (_effectLock) { effect = _effect; }
                if (effect != null)
                {
                    try { effect.Render(f, _ctx); }
                    catch (Exception ex)
                    {
                        RaiseLog("效果 " + effect.Name + " 异常: " + ex.Message);
                    }
                }

                if (Brightness < 0.999)
                {
                    ApplyBrightness(f, Brightness);
                }

                IDeviceAdapter[] devs;
                lock (_deviceLock) { devs = _devices.ToArray(); }
                for (int i = 0; i < devs.Length; i++)
                {
                    try { devs[i].Apply(f); }
                    catch (Exception ex) { devs[i].NoteError(ex.Message); }
                }

                lock (_previewLock)
                {
                    f.CopyTo(_preview);
                    _preview.Live = true;
                    _preview.FrameCounter = frames;
                }

                _ctx.Keys.Decay(t, 3.0);
                frames++;
                now = _clock.Elapsed.TotalSeconds;
                if (now - fpsMark >= 2.0)
                {
                    _fps = frames / (now - fpsMark);
                    frames = 0;
                    fpsMark = now;
                }
                Interlocked.Exchange(ref _frames, frames);
            }
        }

        private static void ApplyBrightness(Frame f, double b)
        {
            for (int i = 0; i < Frame.KeyCount; i++) f.Keyboard[i] = f.Keyboard[i].Scale(b);
            f.Laptop = f.Laptop.Scale(b);
            f.Headset = f.Headset.Scale(b);
        }

        /// <summary>Copy of the last live frame (for the UI preview).</summary>
        public void Snapshot(PreviewData dst)
        {
            lock (_previewLock) { _preview.CopyTo(dst); }
        }

        /// <summary>
        /// Render a preview without touching devices. Used while stopped so the UI
        /// still shows what the current effect looks like.
        /// </summary>
        public void RenderPreview(PreviewData dst, double t)
        {
            Frame f = _idleFrame;
            f.T = t;
            if (Uniform)
            {
                for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = 0.5;
                f.LaptopX = 0.5;
                f.HeadsetX = 0.5;
            }
            else
            {
                for (int i = 0; i < Frame.KeyCount; i++) f.KeyX[i] = _layout.GlobalX(i);
                f.LaptopX = 0.37;
                f.HeadsetX = 0.065;
            }
            IEffect effect;
            lock (_effectLock) { effect = _effect; }
            if (effect != null)
            {
                try { effect.Render(f, _ctx); } catch { }
            }
            if (Brightness < 0.999) ApplyBrightness(f, Brightness);
            lock (_previewLock)
            {
                f.CopyTo(dst);
                dst.Live = false;
            }
        }

        /// <summary>Keyboard hits are stored as desk positions, so all effects share them.</summary>
        public void AddKeyHit(double localX, double deskStart, double deskEnd)
        {
            double x = deskStart + Fx.Clamp01(localX) * (deskEnd - deskStart);
            Keys.Add(x, _clock.Elapsed.TotalSeconds - _t0);
        }

        private void RaiseLog(string msg)
        {
            Action<string> h = Log;
            if (h != null) { try { h(msg); } catch { } }
        }

        public void Dispose()
        {
            Stop();
            ClearDevices();
        }
    }
}
