// PrismSync native app - owns the three device adapters: detect, open, retry, close.
using System;
using System.Threading;

namespace PrismSync
{
    public sealed class DeviceRuntimeState
    {
        public string Id;
        public string Name;
        public string Detail;
        public bool SingleZone;
        public bool Enabled;
        public bool Alive;
        public string Error;
        public string Status;
    }

    public sealed class DeviceHost : IDisposable
    {
        private readonly Engine _engine;
        private readonly bool _admin;
        private readonly object _sync = new object();
        private readonly Timer _retryTimer;

        private IDeviceAdapter _keyboard;
        private IDeviceAdapter _headset;
        private IDeviceAdapter _laptop;

        public event Action<string> Log;

        public bool KeyboardEnabled = true;
        public bool HeadsetEnabled = true;
        public bool LaptopEnabled = true;
        public int HeadsetMode;

        public DeviceHost(Engine engine, bool admin)
        {
            _engine = engine;
            _admin = admin;
            _retryTimer = new Timer(delegate { RetryTick(); }, null, 6000, 6000);
        }

        public void Configure(bool keyboard, bool headset, bool laptop, int headsetMode)
        {
            KeyboardEnabled = keyboard;
            HeadsetEnabled = headset;
            LaptopEnabled = laptop;
            HeadsetMode = headsetMode;
            Rescan(false);
        }

        /// <summary>Re-evaluate every device. force closes and reopens everything.</summary>
        public void Rescan(bool force)
        {
            lock (_sync)
            {
                if (force) CloseAllLocked();
                ApplyLocked();
            }
        }

        private void RetryTick()
        {
            if (!Monitor.TryEnter(_sync)) return;
            try { ApplyLocked(); }
            catch { }
            finally { Monitor.Exit(_sync); }
        }

        private void ApplyLocked()
        {
            bool kBefore = IsAlive(_keyboard);
            bool hBefore = IsAlive(_headset);
            bool lBefore = IsAlive(_laptop);

            ApplyOne("keyboard", KeyboardEnabled, ref _keyboard,
                delegate { return new Hero68Device(_engine.Layout); });
            ApplyOne("headset", HeadsetEnabled, ref _headset,
                delegate { return new MchoseDevice(HeadsetMode); });
            ApplyOne("laptop", LaptopEnabled, ref _laptop,
                delegate { return new ClevoDevice(); });

            LogTransition("键盘", kBefore, IsAlive(_keyboard), _keyboard);
            LogTransition("耳机", hBefore, IsAlive(_headset), _headset);
            LogTransition("笔记本", lBefore, IsAlive(_laptop), _laptop);
        }

        private void ApplyOne(string id, bool enabled, ref IDeviceAdapter slot,
                              Func<IDeviceAdapter> factory)
        {
            if (!enabled)
            {
                if (slot != null)
                {
                    _engine.RemoveDevice(slot);
                    try { slot.Close(); } catch { }
                    slot = null;
                }
                return;
            }
            if (slot != null && slot.IsOpen) return;
            if (slot != null)
            {
                _engine.RemoveDevice(slot);
                try { slot.Close(); } catch { }
                slot = null;
            }
            IDeviceAdapter dev = factory();
            bool ok;
            try { ok = dev.Open(); }
            catch (Exception ex) { dev.NoteError(ex.Message); ok = false; }
            slot = dev;
            if (ok) _engine.AddDevice(dev);
            else RaiseLog(dev.Name + " 未连接: " + dev.LastError);
        }

        private void LogTransition(string label, bool before, bool after, IDeviceAdapter dev)
        {
            if (before == after) return;
            if (after)
            {
                RaiseLog(label + " 已连接" +
                    (dev != null && dev.Detail.Length > 0 ? " (" + dev.Detail + ")" : ""));
            }
            else
            {
                string err = dev != null ? dev.LastError : "";
                RaiseLog(label + " 已断开" + (string.IsNullOrEmpty(err) ? "" : "：" + err));
            }
        }

        private static bool IsAlive(IDeviceAdapter dev)
        {
            return dev != null && dev.IsOpen;
        }

        public void SetEnabled(string id, bool enabled)
        {
            if (id == "keyboard") KeyboardEnabled = enabled;
            else if (id == "headset") HeadsetEnabled = enabled;
            else if (id == "laptop") LaptopEnabled = enabled;
            Rescan(false);
        }

        public void SetHeadsetMode(int mode)
        {
            HeadsetMode = mode;
            MchoseDevice m = _headset as MchoseDevice;
            if (m != null) m.Mode = mode;
        }

        public DeviceRuntimeState[] States()
        {
            IDeviceAdapter k, h, l;
            lock (_sync)
            {
                // opening a device is quick, but never block the UI for a long time
                k = _keyboard;
                h = _headset;
                l = _laptop;
            }
            return new DeviceRuntimeState[]
            {
                MakeState("headset", "耳机", "MCHOSE V9 Turbo+", true, HeadsetEnabled, h),
                MakeState("laptop", "笔记本", "X15 AT23 键盘背光", true, LaptopEnabled, l),
                MakeState("keyboard", "键盘", "AULA HERO 68 HE", false, KeyboardEnabled, k)
            };
        }

        private static DeviceRuntimeState MakeState(string id, string name, string detail,
            bool single, bool enabled, IDeviceAdapter dev)
        {
            DeviceRuntimeState s = new DeviceRuntimeState();
            s.Id = id;
            s.Name = name;
            s.Detail = detail;
            s.SingleZone = single;
            s.Enabled = enabled;
            s.Alive = dev != null && dev.IsOpen;
            s.Error = dev != null ? dev.LastError : "";
            s.Status = dev != null ? dev.Status : "";
            return s;
        }

        public void CloseAll()
        {
            lock (_sync) { CloseAllLocked(); }
        }

        private void CloseAllLocked()
        {
            CloseOne(ref _keyboard);
            CloseOne(ref _headset);
            CloseOne(ref _laptop);
        }

        private void CloseOne(ref IDeviceAdapter slot)
        {
            if (slot == null) return;
            _engine.RemoveDevice(slot);
            try { slot.Close(); } catch { }
            slot = null;
        }

        private void RaiseLog(string message)
        {
            Action<string> h = Log;
            if (h != null) { try { h(message); } catch { } }
        }

        public void Dispose()
        {
            try { _retryTimer.Dispose(); } catch { }
            CloseAll();
        }
    }
}
