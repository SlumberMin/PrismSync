// PrismSync native app - user settings persisted to %APPDATA%\PrismSync\settings.json
using System;
using System.IO;
using System.Text;

namespace PrismSync
{
    public sealed class Settings
    {
        public string EffectId = EffectRegistry.DefaultId;
        public double Speed = 1.0;
        public double Brightness = 1.0;
        public Rgb Color = new Rgb(255, 0, 60);
        public bool Uniform;
        public int TargetFps = 30;
        public bool KeyboardEnabled = true;
        public bool HeadsetEnabled = true;
        public bool LaptopEnabled = true;
        /// <summary>0 solid, 1 breathe, 2 star, 3 cycle, 4 wave (headset firmware).</summary>
        public int HeadsetMode = 0;
        public bool CloseToTray = true;
        public bool AutoElevate = true;
        public bool StartRunning = true;
        public bool AutoKillHub = true;
        public bool AudioReactiveEnabled = true;
        public bool ScreenReactiveEnabled = true;
        public int WindowX = -1;
        public int WindowY = -1;

        private static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDir, "settings.json"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return s;
                string json = File.ReadAllText(path, Encoding.UTF8);
                System.Collections.Generic.Dictionary<string, object> o = MiniJson.ParseObject(json);
                s.EffectId = Str(o, "effect", s.EffectId);
                s.Speed = MiniJson.ToDouble(o, "speed", s.Speed);
                s.Brightness = MiniJson.ToDouble(o, "brightness", s.Brightness);
                s.Color = Rgb.FromHex(Str(o, "color", s.Color.ToHex()));
                s.Uniform = Bool(o, "uniform", s.Uniform);
                s.TargetFps = (int)MiniJson.ToDouble(o, "fps", s.TargetFps);
                s.KeyboardEnabled = Bool(o, "keyboard", s.KeyboardEnabled);
                s.HeadsetEnabled = Bool(o, "headset", s.HeadsetEnabled);
                s.LaptopEnabled = Bool(o, "laptop", s.LaptopEnabled);
                s.HeadsetMode = (int)MiniJson.ToDouble(o, "headsetMode", s.HeadsetMode);
                s.CloseToTray = Bool(o, "closeToTray", s.CloseToTray);
                s.AutoElevate = Bool(o, "autoElevate", s.AutoElevate);
                s.StartRunning = Bool(o, "startRunning", s.StartRunning);
                s.AutoKillHub = Bool(o, "autoKillHub", s.AutoKillHub);
                s.AudioReactiveEnabled = Bool(o, "audioReactive", s.AudioReactiveEnabled);
                s.ScreenReactiveEnabled = Bool(o, "screenReactive", s.ScreenReactiveEnabled);
                s.WindowX = (int)MiniJson.ToDouble(o, "windowX", s.WindowX);
                s.WindowY = (int)MiniJson.ToDouble(o, "windowY", s.WindowY);
                if (EffectRegistry.FindOrNull(s.EffectId) == null) s.EffectId = EffectRegistry.DefaultId;
                s.Speed = Fx.Clamp(s.Speed, 0.3, 2.5);
                s.Brightness = Fx.Clamp(s.Brightness, 0.05, 1.0);
                s.TargetFps = (int)Fx.Clamp(s.TargetFps, 15, 60);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{\n");
                sb.Append("  \"effect\": \"").Append(Escape(EffectId)).Append("\",\n");
                sb.Append("  \"speed\": ").Append(Speed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"brightness\": ").Append(Brightness.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(",\n");
                sb.Append("  \"color\": \"").Append(Color.ToHex()).Append("\",\n");
                sb.Append("  \"uniform\": ").Append(Uniform ? "true" : "false").Append(",\n");
                sb.Append("  \"fps\": ").Append(TargetFps).Append(",\n");
                sb.Append("  \"keyboard\": ").Append(KeyboardEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"headset\": ").Append(HeadsetEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"laptop\": ").Append(LaptopEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"headsetMode\": ").Append(HeadsetMode).Append(",\n");
                sb.Append("  \"closeToTray\": ").Append(CloseToTray ? "true" : "false").Append(",\n");
                sb.Append("  \"autoElevate\": ").Append(AutoElevate ? "true" : "false").Append(",\n");
                sb.Append("  \"startRunning\": ").Append(StartRunning ? "true" : "false").Append(",\n");
                sb.Append("  \"autoKillHub\": ").Append(AutoKillHub ? "true" : "false").Append(",\n");
                sb.Append("  \"audioReactive\": ").Append(AudioReactiveEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"screenReactive\": ").Append(ScreenReactiveEnabled ? "true" : "false").Append(",\n");
                sb.Append("  \"windowX\": ").Append(WindowX).Append(",\n");
                sb.Append("  \"windowY\": ").Append(WindowY).Append("\n");
                sb.Append("}\n");
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static string Str(System.Collections.Generic.Dictionary<string, object> o, string key, string dflt)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return dflt;
        }

        private static bool Bool(System.Collections.Generic.Dictionary<string, object> o, string key, bool dflt)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                string s = Convert.ToString(v);
                return s == "true" || s == "1" || s == "True";
            }
            return dflt;
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
