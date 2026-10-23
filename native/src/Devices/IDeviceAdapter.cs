// PrismSync native app - device adapter contract.
using System;

namespace PrismSync
{
    /// <summary>A lighting device we can push frames to.</summary>
    public interface IDeviceAdapter
    {
        /// <summary>Stable id: "keyboard" / "headset" / "laptop".</summary>
        string Id { get; }
        /// <summary>Chinese display name, e.g. "键盘".</summary>
        string Name { get; }
        /// <summary>Model string shown under the name.</summary>
        string Detail { get; }
        /// <summary>True when the device only has one controllable zone.</summary>
        bool SingleZone { get; }

        bool IsOpen { get; }
        string LastError { get; }
        long Applied { get; }
        long Failed { get; }
        /// <summary>Extra status line, e.g. "固件模式: 呼吸".</summary>
        string Status { get; }

        /// <summary>Try to open the device. False on failure (LastError explains).</summary>
        bool Open();
        void Close();
        void Apply(Frame frame);
        void NoteError(string message);
    }
}
