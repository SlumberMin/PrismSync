// PrismSync native app - effect catalogue + factory (UI reads this list).
//
// The catalogue is the public contract with the UI: ids, Chinese labels and the
// one-line hints are stable and must not change without telling the UI owner.
// Create() is the only place where the generic UI knobs (speed slider 0.3..2.5,
// base colour) are turned into effect parameters.
using System;

namespace PrismSync
{
    public sealed class EffectSpec
    {
        public string Id;
        public string Name;
        public string Hint;
        /// <summary>True when the effect uses the user-picked base colour.</summary>
        public bool UsesColor;

        public EffectSpec(string id, string name, string hint, bool usesColor)
        {
            Id = id; Name = name; Hint = hint; UsesColor = usesColor;
        }
    }

    public static class EffectRegistry
    {
        public const string DefaultId = "trio";

        public static readonly EffectSpec[] Specs = new EffectSpec[]
        {
            new EffectSpec("trio",    "三连流",   "一条色带从耳机流过笔记本、再扫过键盘", false),
            new EffectSpec("aurora",  "极光",     "青紫色带柔和翻涌，久看也不刺眼", false),
            new EffectSpec("duo",     "双色流转", "两种颜色在桌面上互相推挤", false),
            new EffectSpec("flow",    "流光",     "整条彩虹横向滑动，全亮无暗区", false),
            new EffectSpec("wave",    "波浪",     "一道亮光扫过，其余保持微亮", false),
            new EffectSpec("comet",   "彗星",     "亮头带长尾，一直亮着", false),
            new EffectSpec("rainbow", "七彩同步", "三台设备完全同色，一起变色", false),
            new EffectSpec("hue",     "色相渐变", "每台设备显示同一条渐变，整体缓慢转色", false),
            new EffectSpec("breathe", "呼吸",     "一起缓慢明暗起伏", true),
            new EffectSpec("static",  "常亮",     "固定一个颜色", true),
            new EffectSpec("plasma",  "星云",     "多层噪声叠加出的流动云彩", false),
            new EffectSpec("stars",   "繁星点点", "随机星光一明一暗，星点缓慢漂移", false),
            new EffectSpec("meteor",  "流星雨",   "几颗不同颜色的流星同时划过", false),
            new EffectSpec("fire",    "焰火",     "橙红色火焰向上跳动，带随机闪烁", false),
            new EffectSpec("ripple",  "涟漪",     "一圈圈光波从中心向外扩散", false),
            new EffectSpec("keyhit",  "击键涟漪", "每次敲键盘，光波从那个键向两侧扩散", false),
            new EffectSpec("screen",  "屏幕氛围", "取屏幕颜色，桌面和键盘跟着画面变", false),
            new EffectSpec("audio",   "音频律动", "麦克风拾音，声音越大灯越亮", false)
        };

        public static EffectSpec Find(string id)
        {
            EffectSpec s = FindOrNull(id);
            return s != null ? s : Specs[0];
        }

        public static EffectSpec FindOrNull(string id)
        {
            for (int i = 0; i < Specs.Length; i++) if (Specs[i].Id == id) return Specs[i];
            return null;
        }

        /// <summary>
        /// Python effects.py: _speed(base, mult) = max(0.02, base * max(0.1, mult)).
        /// The UI slider is 0.3..2.5, so the result is a per-effect animation
        /// speed; never use the slider value directly as a frequency.
        /// </summary>
        private static double Speed(double baseSpeed, double mult)
        {
            if (mult < 0.1) mult = 0.1;
            double s = baseSpeed * mult;
            return s < 0.02 ? 0.02 : s;
        }

        /// <summary>
        /// Build one effect. Never throws for an unknown id (falls back to the
        /// default "trio" spec) because settings may contain an old effect id.
        /// </summary>
        public static IEffect Create(string id, double speed, Rgb color)
        {
            EffectSpec spec = Find(id);
            switch (spec.Id)
            {
                // --- ten ported effects; base speeds copied from effects.py -----
                case "trio":    return new TrioEffect(spec, Speed(0.18, speed));
                case "aurora":  return new AuroraEffect(spec, Speed(0.10, speed));
                case "duo":     return new DuoEffect(spec, Speed(0.14, speed));
                case "flow":    return new FlowEffect(spec, Speed(0.12, speed));
                case "wave":    return new WaveEffect(spec, Speed(0.35, speed));
                case "comet":   return new CometEffect(spec, Speed(0.30, speed));
                case "rainbow": return new RainbowEffect(spec, Speed(0.12, speed));
                case "hue":     return new HueSweepEffect(spec, Speed(0.20, speed));
                case "breathe":
                    // Python: period = max(0.6, 2.4 / speedMult)
                    return new BreatheEffect(spec, color,
                        Math.Max(0.6, 2.4 / (speed < 0.1 ? 0.1 : speed)));
                case "static":  return new SolidEffect(spec, color);

                // --- eight new effects; base speeds chosen for 30 fps smoothness --
                case "plasma":  return new PlasmaEffect(spec, Speed(0.10, speed));
                case "stars":   return new StarsEffect(spec, Speed(0.16, speed));
                case "meteor":  return new MeteorEffect(spec, Speed(0.22, speed));
                case "fire":    return new FireEffect(spec, Speed(0.35, speed));
                case "ripple":  return new RippleEffect(spec, Speed(0.40, speed));
                case "keyhit":  return new KeyHitEffect(spec, Speed(0.55, speed));
                case "screen":  return new ScreenAmbientEffect(spec);
                case "audio":   return new AudioPulseEffect(spec, Speed(1.00, speed));
            }

            // Unknown id: replicate the default effect (trio).
            return new TrioEffect(spec, Speed(0.18, speed));
        }
    }
}
