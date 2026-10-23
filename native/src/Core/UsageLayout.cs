// PrismSync native app - standard position of a HID keyboard usage id.
//
// This mirrors the calibrated 68-key HERO 68 HE layout (rows 15/15/14/14/10, with
// the Ins/Del/PgUp/PgDn column on the right and the arrow cluster bottom-right).
// The calibration tool (native/tools/keycal) uses it to turn a pressed key into a
// row / horizontal position; the app itself uses KeyLayout's calibrated table.
using System;

namespace PrismSync
{
    public static class UsageLayout
    {
        private struct K
        {
            public int Usage;
            public double Width;
            public K(int usage, double width) { Usage = usage; Width = width; }
        }

        private static readonly K[][] Rows = new K[][]
        {
            new K[]
            {
                new K(0x29,1), new K(0x1E,1), new K(0x1F,1), new K(0x20,1), new K(0x21,1),
                new K(0x22,1), new K(0x23,1), new K(0x24,1), new K(0x25,1), new K(0x26,1),
                new K(0x27,1), new K(0x2D,1), new K(0x2E,1), new K(0x2A,2), new K(0x49,1)
            },
            new K[]
            {
                new K(0x2B,1.5), new K(0x14,1), new K(0x1A,1), new K(0x08,1), new K(0x15,1),
                new K(0x17,1), new K(0x1C,1), new K(0x18,1), new K(0x0C,1), new K(0x12,1),
                new K(0x13,1), new K(0x2F,1), new K(0x30,1), new K(0x31,1.5), new K(0x4C,1)
            },
            new K[]
            {
                new K(0x39,1.75), new K(0x04,1), new K(0x16,1), new K(0x07,1), new K(0x09,1),
                new K(0x0A,1), new K(0x0B,1), new K(0x0D,1), new K(0x0E,1), new K(0x0F,1),
                new K(0x33,1), new K(0x34,1), new K(0x28,2.25), new K(0x4B,1)
            },
            new K[]
            {
                new K(0xE1,2.25), new K(0x1D,1), new K(0x1B,1), new K(0x06,1), new K(0x19,1),
                new K(0x05,1), new K(0x11,1), new K(0x10,1), new K(0x36,1), new K(0x37,1),
                new K(0x38,1), new K(0xE5,1.75), new K(0x52,1), new K(0x4E,1)
            },
            new K[]
            {
                new K(0xE0,1.25), new K(0xE3,1.25), new K(0xE2,1.25), new K(0x2C,6.25),
                new K(0xE6,1.25), new K(0x00,1), new K(0xE4,1.25),
                new K(0x50,1), new K(0x51,1), new K(0x4F,1)
            }
        };

        /// <summary>Approximate horizontal position (0..1) of a HID usage id.</summary>
        public static bool TryLocalX(int usage, out double x)
        {
            int row;
            double width;
            return TryLayout(usage, out row, out x, out width);
        }

        /// <summary>Row, horizontal position and key width of a HID usage id.</summary>
        public static bool TryLayout(int usage, out int row, out double x, out double width)
        {
            x = 0;
            width = 1;
            row = 0;
            for (int r = 0; r < Rows.Length; r++)
            {
                double total = 0;
                for (int i = 0; i < Rows[r].Length; i++) total += Rows[r][i].Width;
                double acc = 0;
                for (int i = 0; i < Rows[r].Length; i++)
                {
                    if (Rows[r][i].Usage == usage)
                    {
                        row = r;
                        width = Rows[r][i].Width;
                        x = (acc + width * 0.5) / total;
                        return true;
                    }
                    acc += Rows[r][i].Width;
                }
            }
            return false;
        }

        /// <summary>Human readable keycap label for a usage id (calibration log).</summary>
        public static string Name(int usage)
        {
            if (usage >= 0x04 && usage <= 0x1D) return ((char)('A' + (usage - 0x04))).ToString();
            if (usage >= 0x1E && usage <= 0x27) return ((char)('0' + (usage - 0x1E))).ToString();
            switch (usage)
            {
                case 0x00: return "Fn";
                case 0x28: return "Enter";
                case 0x29: return "Esc";
                case 0x2A: return "Backspace";
                case 0x2B: return "Tab";
                case 0x2C: return "Space";
                case 0x2D: return "-";
                case 0x2E: return "=";
                case 0x2F: return "[";
                case 0x30: return "]";
                case 0x31: return "\\";
                case 0x33: return ";";
                case 0x34: return "'";
                case 0x35: return "`";
                case 0x36: return ",";
                case 0x37: return ".";
                case 0x38: return "/";
                case 0x39: return "Caps";
                case 0x49: return "Insert";
                case 0x4A: return "Home";
                case 0x4B: return "PageUp";
                case 0x4C: return "Delete";
                case 0x4D: return "End";
                case 0x4E: return "PageDown";
                case 0x4F: return "Right";
                case 0x50: return "Left";
                case 0x51: return "Down";
                case 0x52: return "Up";
                case 0xE0: return "LCtrl";
                case 0xE1: return "LShift";
                case 0xE2: return "LAlt";
                case 0xE3: return "LWin";
                case 0xE4: return "RCtrl";
                case 0xE5: return "RShift";
                case 0xE6: return "RAlt";
                case 0xE7: return "RWin";
            }
            return "usage 0x" + usage.ToString("X2");
        }
    }
}
