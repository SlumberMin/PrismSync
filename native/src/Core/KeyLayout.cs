// PrismSync native app - physical layout of the HERO 68 HE's 68 addressable keys.
//
// Table recovered by lighting every LED id one at a time and having the user press
// the lit key (native/tools/keycal), then cross-checked against the official key
// map picture. Result: 68 keys, including the Fn key (LED 72, which reports no HID
// usage) and LED 76 (Left arrow), which the old 67-key table missed.
//
// The table is in physical (row-major) order, so the preview and the hardware use
// exactly the same order and positions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace PrismSync
{
    public sealed class KeyLayout
    {
        public const int Count = 68;
        public const int Rows = 5;
        /// <summary>Convenience alias for the UI (instance access).</summary>
        public int RowCount { get { return Rows; } }
        public string Source = "HERO 68 HE (校准表)";

        /// <summary>LED id of every key in physical (row-major) order.</summary>
        public readonly int[] LedId = new int[Count];
        /// <summary>Horizontal position inside the keyboard, 0..1.</summary>
        public readonly double[] LocalX = new double[Count];
        /// <summary>Physical row (0 = top).</summary>
        public readonly int[] Row = new int[Count];
        /// <summary>Relative key width for preview drawing.</summary>
        public readonly double[] Width = new double[Count];
        /// <summary>HID keyboard usage id (0 for Fn, which never reports).</summary>
        public readonly int[] Usage = new int[Count];

        // Row-major LED ids, from the calibration run.
        private static readonly int[] TableIds = new int[]
        {
            1, 15,16,17,18,19,20,21,22,23,24,25,26,27, 98,
            28,29,30,31,32,33,34,35,36,37,38,39,40,41, 99,
            42,43,44,45,46,47,48,49,50,51,52,53,54, 102,
            55,56,57,58,59,60,61,62,63,64,65,66, 74,103,
            67,68,69,70,71,72,73, 76,75,77
        };

        // HID usages in the same order (0 = Fn).
        private static readonly int[] TableUsage = new int[]
        {
            0x29,0x1E,0x1F,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x2D,0x2E,0x2A,0x49,
            0x2B,0x14,0x1A,0x08,0x15,0x17,0x1C,0x18,0x0C,0x12,0x13,0x2F,0x30,0x31,0x4C,
            0x39,0x04,0x16,0x07,0x09,0x0A,0x0B,0x0D,0x0E,0x0F,0x33,0x34,0x28,0x4B,
            0xE1,0x1D,0x1B,0x06,0x19,0x05,0x11,0x10,0x36,0x37,0x38,0xE5,0x52,0x4E,
            0xE0,0xE3,0xE2,0x2C,0xE6,0x00,0xE4,0x50,0x51,0x4F
        };

        private static readonly int[] RowStart = new int[] { 0, 15, 30, 44, 58 };
        private static readonly int[] RowSizes = new int[] { 15, 15, 14, 14, 10 };

        // Relative key widths per row (65% compact board with a right-hand nav column).
        private static readonly double[][] RowWidths = new double[][]
        {
            new double[] { 1,1,1,1,1,1,1,1,1,1,1,1,1,2,1 },
            new double[] { 1.5,1,1,1,1,1,1,1,1,1,1,1,1,1.5,1 },
            new double[] { 1.75,1,1,1,1,1,1,1,1,1,1,1,2.25,1 },
            new double[] { 2.25,1,1,1,1,1,1,1,1,1,1,1.75,1,1 },
            new double[] { 1.25,1.25,1.25,6.25,1.25,1,1.25,1,1,1 }
        };

        public KeyLayout()
        {
            BuildFromTable();
            TryLoadKeymapOverride();
        }

        private void BuildFromTable()
        {
            double[] rowTotal = new double[Rows];
            for (int r = 0; r < Rows; r++)
            {
                double t = 0;
                for (int c = 0; c < RowSizes[r]; c++) t += RowWidths[r][c];
                rowTotal[r] = t;
            }
            for (int r = 0; r < Rows; r++)
            {
                double acc = 0;
                for (int c = 0; c < RowSizes[r]; c++)
                {
                    int i = RowStart[r] + c;
                    LedId[i] = TableIds[i];
                    Usage[i] = TableUsage[i];
                    Row[i] = r;
                    Width[i] = RowWidths[r][c];
                    LocalX[i] = (acc + Width[i] * 0.5) / rowTotal[r];
                    acc += Width[i];
                }
            }
            Source = "HERO 68 HE 校准表";
        }

        /// <summary>
        /// Optional override: a keymap produced by a later calibration run. Only a
        /// complete 68-key table is accepted - a partial table would silently
        /// scramble the physical order.
        /// </summary>
        private void TryLoadKeymapOverride()
        {
            string json = ReadKeymapJson();
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                Dictionary<string, object> root = MiniJson.ParseObject(json);
                object keysObj;
                if (!root.TryGetValue("keys", out keysObj)) return;
                List<object> keys = keysObj as List<object>;
                if (keys == null || keys.Count != Count) return;

                int[] ids = new int[Count];
                double[] xs = new double[Count];
                int[] rows = new int[Count];
                double[] widths = new double[Count];
                int[] usages = new int[Count];
                bool[] seen = new bool[Count];
                for (int i = 0; i < Count; i++)
                {
                    Dictionary<string, object> o = keys[i] as Dictionary<string, object>;
                    if (o == null) return;
                    ids[i] = (int)MiniJson.ToDouble(o, "id", 0);
                    xs[i] = MiniJson.ToDouble(o, "x", -1);
                    rows[i] = (int)MiniJson.ToDouble(o, "row", -1);
                    widths[i] = MiniJson.ToDouble(o, "width", 0);
                    usages[i] = (int)MiniJson.ToDouble(o, "usage", 0);
                    if (ids[i] <= 0 || xs[i] < 0 || rows[i] < 0 || rows[i] >= Rows) return;
                }
                // every LED id must appear exactly once
                for (int i = 0; i < Count; i++)
                {
                    for (int j = 0; j < Count; j++)
                        if (ledEquals(ids[i], TableIds[j])) { if (seen[j]) return; seen[j] = true; break; }
                }
                for (int j = 0; j < Count; j++) if (!seen[j]) return;

                for (int i = 0; i < Count; i++)
                {
                    LedId[i] = ids[i];
                    LocalX[i] = Fx.Clamp01(xs[i]);
                    Row[i] = rows[i];
                    Width[i] = widths[i] > 0 ? widths[i] : 1.0;
                    Usage[i] = usages[i];
                }
                object src;
                if (root.TryGetValue("source", out src) && src != null) Source = src.ToString();
            }
            catch { }
        }

        private static bool ledEquals(int a, int b) { return a == b; }

        private static string ReadKeymapJson()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("keymap.json"))
                {
                    if (s != null)
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                            return r.ReadToEnd();
                }
            }
            catch { }

            string dir = AppPaths.ExeDir;
            string[] candidates = new string[]
            {
                Path.Combine(dir, "keymap.json"),
                Path.Combine(dir, "keymap", "keymap.json")
            };
            foreach (string p in candidates)
            {
                try { if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8); }
                catch { }
            }
            return null;
        }

        public double GlobalX(int index) { return 0.60 + LocalX[index] * 0.40; }

        public int IndexOfLed(int id)
        {
            for (int i = 0; i < Count; i++) if (LedId[i] == id) return i;
            return -1;
        }

        /// <summary>Physical key index for a HID usage id, or -1.</summary>
        public int IndexOfUsage(int usage)
        {
            if (usage == 0) return -1;
            for (int i = 0; i < Count; i++) if (Usage[i] == usage) return i;
            return -1;
        }
    }

    /// <summary>Tiny JSON reader (objects / arrays / numbers / strings / bools).</summary>
    public static class MiniJson
    {
        private static string _s;
        private static int _i;

        public static Dictionary<string, object> ParseObject(string text)
        {
            _s = text;
            _i = 0;
            object v = ParseValue();
            Dictionary<string, object> d = v as Dictionary<string, object>;
            return d ?? new Dictionary<string, object>();
        }

        public static double ToDouble(Dictionary<string, object> o, string key, double dflt)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return dflt;
            if (v is double) return (double)v;
            double d;
            if (double.TryParse(Convert.ToString(v), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return dflt;
        }

        private static void Skip()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private static object ParseValue()
        {
            Skip();
            if (_i >= _s.Length) return null;
            char c = _s[_i];
            if (c == '{') return ParseObj();
            if (c == '[') return ParseArr();
            if (c == '"') return ParseString();
            if (c == 't') { _i += 4; return true; }
            if (c == 'f') { _i += 5; return false; }
            if (c == 'n') { _i += 4; return null; }
            return ParseNumber();
        }

        private static Dictionary<string, object> ParseObj()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            _i++; // {
            while (_i < _s.Length)
            {
                Skip();
                if (_i < _s.Length && _s[_i] == '}') { _i++; break; }
                if (_i >= _s.Length || _s[_i] != '"') { _i++; continue; }
                string key = ParseString();
                Skip();
                if (_i < _s.Length && _s[_i] == ':') _i++;
                d[key] = ParseValue();
                Skip();
                if (_i < _s.Length && _s[_i] == ',') _i++;
            }
            return d;
        }

        private static List<object> ParseArr()
        {
            List<object> list = new List<object>();
            _i++; // [
            while (_i < _s.Length)
            {
                Skip();
                if (_i < _s.Length && _s[_i] == ']') { _i++; break; }
                list.Add(ParseValue());
                Skip();
                if (_i < _s.Length && _s[_i] == ',') _i++;
            }
            return list;
        }

        private static string ParseString()
        {
            StringBuilder sb = new StringBuilder();
            _i++; // opening quote
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (c == '"') break;
                if (c == '\\' && _i < _s.Length)
                {
                    char e = _s[_i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 <= _s.Length)
                            {
                                int code = Convert.ToInt32(_s.Substring(_i, 4), 16);
                                sb.Append((char)code);
                                _i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static double ParseNumber()
        {
            int start = _i;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') _i++;
                else break;
            }
            double d;
            double.TryParse(_s.Substring(start, _i - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out d);
            return d;
        }
    }

    /// <summary>Well-known application paths.</summary>
    public static class AppPaths
    {
        public static string ExeDir
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                        "PrismSync");
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
    }
}
