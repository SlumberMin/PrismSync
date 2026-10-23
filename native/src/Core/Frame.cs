// PrismSync native app - the frame handed from the effect engine to every device.
using System;

namespace PrismSync
{
    /// <summary>
    /// One instant of lighting state.
    ///
    /// The keyboard is the only device with per-LED control (68 keys). The laptop
    /// backlight and the headset are single-zone, so they each get one colour.
    /// KeyX holds the position of every keyboard LED on the shared desk axis
    /// (0 = far left, 1 = far right) so one effect can flow across all devices.
    /// </summary>
    public sealed class Frame
    {
        public const int KeyCount = 68;

        public readonly Rgb[] Keyboard = new Rgb[KeyCount];
        public readonly double[] KeyX = new double[KeyCount];
        public double LaptopX = 0.37;
        public double HeadsetX = 0.065;
        public Rgb Laptop;
        public Rgb Headset;
        public double T;

        public void FillAll(Rgb c)
        {
            for (int i = 0; i < KeyCount; i++) Keyboard[i] = c;
            Laptop = c;
            Headset = c;
        }

        public void CopyTo(PreviewData dst)
        {
            Array.Copy(Keyboard, dst.Keyboard, KeyCount);
            dst.Laptop = Laptop;
            dst.Headset = Headset;
            dst.T = T;
        }
    }

    /// <summary>Read-only copy of the last rendered frame, consumed by the UI at its own pace.</summary>
    public sealed class PreviewData
    {
        public readonly Rgb[] Keyboard = new Rgb[Frame.KeyCount];
        public Rgb Laptop;
        public Rgb Headset;
        public double T;
        public bool Live;
        public long FrameCounter;

        public void CopyTo(PreviewData dst)
        {
            Array.Copy(Keyboard, dst.Keyboard, Frame.KeyCount);
            dst.Laptop = Laptop;
            dst.Headset = Headset;
            dst.T = T;
            dst.Live = Live;
            dst.FrameCounter = FrameCounter;
        }
    }
}
