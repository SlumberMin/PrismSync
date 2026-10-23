// PrismSync native app - application controller: settings + engine + devices + services.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;

namespace PrismSync
{
    public sealed class AppSnapshot
    {
        public bool Running;
        public string EffectId;
        public string EffectName;
        public string EffectHint;
        public bool EffectUsesColor;
        public double Speed;
        public double Brightness;
        public Rgb Color;
        public bool Uniform;
        public int TargetFps;
        public double Fps;
        public long Frames;
        public bool IsAdmin;
        public bool CloseToTray;
        public bool AutoKillHub;
        public int HeadsetMode;
        public bool MicAvailable;
        public float MicLevel;
        public bool ScreenAvailable;
        public bool KeyboardHookAvailable;
        public bool AudioEffectReady;
        public DeviceRuntimeState[] Devices = new DeviceRuntimeState[0];
        public string[] Log = new string[0];
        public PreviewData Preview = new PreviewData();
    }

    public sealed class AppController : IDisposable
    {
        private readonly Settings _settings;
        private readonly Engine _engine;
        private readonly DeviceHost _host;
        private readonly ScreenSampler _screen;
        private readonly AudioMeter _audio;
        private readonly KeyPressMonitor _keys;
        private readonly bool _admin;
        private readonly bool _noDevices;

        private readonly object _logLock = new object();
        private readonly List<string> _log = new List<string>(64);
        private readonly Stopwatch _idleClock = Stopwatch.StartNew();
        private PreviewData _snapshotPreview = new PreviewData();
        private string _effectId;
        private bool _quitting;
        private bool _startedOnce;
        private bool _audioStarted;
        private float _audioLevel;
        private bool _disposed;

        public event Action QuitRequested;
        public event Action<string> LogLine;

        public AppController(bool admin) : this(admin, false) { }

        public AppController(bool admin, bool noDevices)
        {
            _admin = admin;
            _settings = Settings.Load();
            _noDevices = noDevices;
            _engine = new Engine();
            _host = new DeviceHost(_engine, admin);
            _host.Log += AddLog;
            _screen = new ScreenSampler();
            _audio = new AudioMeter();
            _keys = new KeyPressMonitor();
            _effectId = _settings.EffectId;

            _engine.Log += AddLog;
            _engine.AttachScreen(_screen);
            _engine.AttachAudio(_audio);
            _engine.SetTargetFps(_settings.TargetFps);
            _engine.Brightness = _settings.Brightness;
            _engine.Uniform = _settings.Uniform;
            _engine.Context.BaseColor = _settings.Color;

            // --no-devices is a UI-testing switch: it must not be persisted, or the
            // next normal launch would come up with every device switched off.
            _host.Configure(noDevices ? false : _settings.KeyboardEnabled,
                            noDevices ? false : _settings.HeadsetEnabled,
                            noDevices ? false : _settings.LaptopEnabled,
                            _settings.HeadsetMode);
            RebuildEffect(true);
            StartServices();

            AddLog("棱镜同步已就绪" + (admin ? "（管理员）" : "（普通权限，笔记本背光需管理员）"));
        }

        public EffectSpec[] Effects { get { return EffectRegistry.Specs; } }
        public string DefaultEffectId { get { return EffectRegistry.DefaultId; } }
        public bool IsAdmin { get { return _admin; } }
        public bool Quitting { get { return _quitting; } }
        public Engine Engine { get { return _engine; } }
        public Settings Settings { get { return _settings; } }

        private void StartServices()
        {
            try
            {
                if (_settings.ScreenReactiveEnabled) _screen.Grab();
            }
            catch { }

            try
            {
                _keys.KeyEvent += OnKeyEvent;
                if (_keys.Start()) AddLog("键盘监听已启动");
                else AddLog("键盘监听不可用: " + _keys.LastError);
            }
            catch (Exception ex) { AddLog("键盘监听异常: " + ex.Message); }
        }

        private void OnKeyEvent(int usage, bool down)
        {
            if (!down) return;
            // The keyboard layout is calibrated, so a keystroke maps straight onto a
            // physical key (Fn never reports a usage id).
            int index = _engine.Layout.IndexOfUsage(usage);
            if (index < 0) return;
            _engine.AddKeyHit(_engine.Layout.LocalX[index], 0.60, 1.0);
        }

        private void RebuildEffect(bool resetPhase)
        {
            IEffect effect = EffectRegistry.Create(_effectId, _settings.Speed, _settings.Color);
            _engine.Context.BaseColor = _settings.Color;
            _engine.SetEffect(effect, resetPhase);
        }

        private void ApplyLiveSettings()
        {
            _engine.Brightness = _settings.Brightness;
            _engine.Uniform = _settings.Uniform;
            _engine.Context.BaseColor = _settings.Color;
            RebuildEffect(false);
        }

        // ---------------------------------------------------------------- actions
        public void SetEffect(string id)
        {
            EffectSpec spec = EffectRegistry.FindOrNull(id);
            if (spec == null) return;
            _settings.EffectId = _effectId = id;
            if (id == "audio") EnsureAudio();
            RebuildEffect(true);
            AddLog("切换效果: " + spec.Name);
            _settings.Save();
        }

        public void SetSpeed(double speed)
        {
            _settings.Speed = Fx.Clamp(speed, 0.3, 2.5);
            RebuildEffect(false);
        }

        public void SetBrightness(double brightness)
        {
            _settings.Brightness = Fx.Clamp(brightness, 0.05, 1.0);
            _engine.Brightness = _settings.Brightness;
        }

        public void SetColor(Rgb color)
        {
            _settings.Color = color;
            ApplyLiveSettings();
        }

        public void SetUniform(bool uniform)
        {
            _settings.Uniform = uniform;
            _engine.Uniform = uniform;
            _settings.Save();
        }

        public void SetTargetFps(int fps)
        {
            _settings.TargetFps = (int)Fx.Clamp(fps, 15, 60);
            _engine.SetTargetFps(_settings.TargetFps);
            _settings.Save();
        }

        public void SetHeadsetMode(int mode)
        {
            _settings.HeadsetMode = mode;
            _host.SetHeadsetMode(mode);
            _settings.Save();
        }

        public void SetDeviceEnabled(string id, bool enabled)
        {
            if (id == "keyboard") _settings.KeyboardEnabled = enabled;
            else if (id == "headset") _settings.HeadsetEnabled = enabled;
            else if (id == "laptop") _settings.LaptopEnabled = enabled;
            _host.SetEnabled(id, enabled);
            _host.Rescan(false);
            _settings.Save();
        }

        public void RescanDevices()
        {
            _host.Rescan(true);
            AddLog("已重新扫描设备");
        }

        public void SetCloseToTray(bool value) { _settings.CloseToTray = value; _settings.Save(); }
        public void SetAutoKillHub(bool value) { _settings.AutoKillHub = value; _settings.Save(); }

        public void Start()
        {
            if (_engine.IsRunning) return;
            _host.Rescan(false);
            if (_settings.EffectId == "audio") EnsureAudio();
            _engine.Start();
            if (!_startedOnce && _settings.AutoKillHub)
            {
                _startedOnce = true;
                ThreadPool.QueueUserWorkItem(delegate { KillMHub(true); });
            }
            _startedOnce = true;
            AddLog("灯效已启动");
        }

        public void Stop()
        {
            if (!_engine.IsRunning) return;
            _engine.Stop();
            _host.CloseAll();
            AddLog("灯效已停止，设备已释放");
        }

        public void Toggle()
        {
            if (_engine.IsRunning) Stop(); else Start();
        }

        private void EnsureAudio()
        {
            if (_audioStarted) return;
            _audioStarted = true;
            try
            {
                if (_audio.Start()) AddLog("麦克风已接入");
                else AddLog("麦克风不可用: " + _audio.LastError);
            }
            catch (Exception ex) { AddLog("麦克风异常: " + ex.Message); }
        }

        public string KillMHub(bool silent)
        {
            string result = HubKiller.Kill();
            if (!silent) AddLog("关闭 M HUB: " + result);
            else AddLog("自动关闭 M HUB: " + result);
            return result;
        }

        // ---------------------------------------------------------------- snapshot
        public AppSnapshot Snapshot()
        {
            AppSnapshot s = new AppSnapshot();
            s.Running = _engine.IsRunning;
            EffectSpec spec = EffectRegistry.Find(_effectId);
            s.EffectId = spec.Id;
            s.EffectName = spec.Name;
            s.EffectHint = spec.Hint;
            s.EffectUsesColor = spec.UsesColor;
            s.Speed = _settings.Speed;
            s.Brightness = _settings.Brightness;
            s.Color = _settings.Color;
            s.Uniform = _settings.Uniform;
            s.TargetFps = _settings.TargetFps;
            EngineStats st = _engine.Stats();
            s.Fps = st.Fps;
            s.Frames = st.Frames;
            s.IsAdmin = _admin;
            s.CloseToTray = _settings.CloseToTray;
            s.AutoKillHub = _settings.AutoKillHub;
            s.HeadsetMode = _settings.HeadsetMode;
            s.MicAvailable = _audio.Available;
            s.MicLevel = _audioStarted ? ReadAudioLevel() : 0f;
            s.ScreenAvailable = _screen.Available;
            s.KeyboardHookAvailable = _keys.Available;
            s.AudioEffectReady = _audio.Available;
            s.Devices = _host.States();
            lock (_logLock)
            {
                int n = Math.Min(6, _log.Count);
                s.Log = new string[n];
                for (int i = 0; i < n; i++) s.Log[i] = _log[_log.Count - n + i];
            }

            if (_engine.IsRunning)
            {
                _engine.Snapshot(_snapshotPreview);
            }
            else
            {
                _engine.RenderPreview(_snapshotPreview, _idleClock.Elapsed.TotalSeconds);
            }
            _snapshotPreview.CopyTo(s.Preview);
            return s;
        }

        private float ReadAudioLevel()
        {
            try
            {
                _audioLevel = _audio.Level;
                return _audioLevel;
            }
            catch { return 0f; }
        }

        private void AddLog(string line)
        {
            string text = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
            lock (_logLock)
            {
                _log.Add(text);
                if (_log.Count > 120) _log.RemoveRange(0, _log.Count - 120);
            }
            Action<string> h = LogLine;
            if (h != null) { try { h(text); } catch { } }
        }

        // ---------------------------------------------------------------- lifecycle
        public void SaveWindowPosition(int x, int y) { _settings.WindowX = x; _settings.WindowY = y; }
        public void SaveSettings() { _settings.Save(); }

        public void Quit()
        {
            if (_quitting) return;
            _quitting = true;
            try { _settings.Save(); } catch { }
            try { _engine.Stop(); } catch { }
            try { _host.CloseAll(); } catch { }
            try { _audio.Stop(); } catch { }
            try { _screen.Dispose(); } catch { }
            try { _keys.Stop(); } catch { }
            try { _host.Dispose(); } catch { }
            AddLog("已退出，设备已释放");
            Action h = QuitRequested;
            if (h != null) { try { h(); } catch { } }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_quitting) Quit();
            _engine.Dispose();
            _audio.Dispose();
            _screen.Dispose();
            _keys.Dispose();
        }
    }

    /// <summary>Stops MCHOSE HUB, which re-pushes its own lighting every ~200 ms.</summary>
    public static class HubKiller
    {
        public static string Kill()
        {
            try
            {
                Process[] procs = Process.GetProcesses();
                int n = 0;
                System.Text.StringBuilder names = new System.Text.StringBuilder();
                for (int i = 0; i < procs.Length; i++)
                {
                    string name = procs[i].ProcessName;
                    if (name.IndexOf("MCHOSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("M HUB", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            procs[i].Kill();
                            n++;
                            if (names.Length > 0) names.Append(", ");
                            names.Append(name);
                        }
                        catch { }
                    }
                }
                if (n == 0) return "M HUB 未在运行";
                return "已关闭 " + names;
            }
            catch (Exception ex) { return "关闭失败: " + ex.Message; }
        }
    }
}
