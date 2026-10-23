// Console probe for the native HID layer (Lead tool, not part of the app).
using System;
using System.Collections.Generic;
using System.Threading;
using PrismSync;

internal static class HidTest
{
    private static string Hex(byte[] b, int n)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(b[i].ToString("X2")).Append(' ');
        return sb.ToString().Trim();
    }

    private static void Main()
    {
        Console.WriteLine("== all HID collections for our VIDs ==");
        foreach (HidDeviceInfo d in HidEnum.EnumerateAll())
        {
            if (d.VendorId != 0x372E && d.VendorId != 0x3837) continue;
            Console.WriteLine("{0:X4}:{1:X4} up=0x{2:X4} usage=0x{3:X4} in={4} out={5} {6}",
                d.VendorId, d.ProductId, d.UsagePage, d.Usage,
                d.InputReportByteLength, d.OutputReportByteLength, d.Product);
            Console.WriteLine("    " + d.Path);
        }

        Console.WriteLine();
        Console.WriteLine("== keyboard ==");
        List<HidDeviceInfo> kbd = HidEnum.Find(0x372E, 0x103E);
        HidDeviceInfo k = null;
        foreach (HidDeviceInfo d in kbd) if (d.UsagePage == 0xFF60) { k = d; break; }
        if (k == null) { Console.WriteLine("keyboard vendor interface NOT FOUND"); }
        else
        {
            using (HidDevice dev = HidDevice.Open(k))
            {
                Console.WriteLine("opened keyboard, outLen=" + k.OutputReportByteLength +
                                  " inLen=" + k.InputReportByteLength);
                byte[] uidQuery = Pkt(0x82, 0x01, null);
                Console.WriteLine("write uid query -> " + dev.Write(uidQuery, 64));
                byte[] buf = new byte[128];
                int n = dev.Read(buf, 1200);
                Console.WriteLine("uid reply (" + n + "): " + Hex(buf, Math.Min(n, 16)));
                if (n >= 13) Console.WriteLine("UID=" + BitConverter.ToString(buf, 7, 6).Replace("-", "").ToLowerInvariant());

                Console.WriteLine("write enable -> " + dev.Write(Pkt(0x01, 0x80, new byte[] { 0x10 }), 64));
                Thread.Sleep(30);
                Console.WriteLine("write custom -> " + dev.Write(Pkt(0x04, 0x02, new byte[] { 0x13, 0x00 }), 64));
                Console.WriteLine("write bright/speed -> " + dev.Write(Pkt(0x04, 0x04, new byte[] { 0x64, 0x04 }), 64));

                // paint all keys a colour (cmd 0x08, one colour segment per packet)
                int[] ids = new int[] { 1 };
                List<int> list = new List<int>();
                list.Add(1);
                for (int i = 15; i <= 75; i++) list.Add(i);
                list.Add(77); list.Add(98); list.Add(99); list.Add(102); list.Add(103);
                ids = list.ToArray();
                byte[] payload = new byte[4 + ids.Length];
                payload[0] = 0x30; payload[1] = 0x00; payload[2] = 0xFF; payload[3] = (byte)ids.Length;
                for (int i = 0; i < ids.Length; i++) payload[4 + i] = (byte)ids[i];
                Console.WriteLine("paint 67 keys -> " + dev.Write(Pkt(0x08, 0x01, payload), 64));
                Thread.Sleep(30);
                Console.WriteLine("commit -> " + dev.Write(Pkt(0x01, 0x00, null), 64));
            }
        }

        Console.WriteLine();
        Console.WriteLine("== headset ==");
        List<HidDeviceInfo> hs = HidEnum.Find(0x3837, 0x6008);
        HidDeviceInfo h = null;
        foreach (HidDeviceInfo d in hs) if (d.UsagePage == 0xFF00) { h = d; break; }
        if (h == null) { Console.WriteLine("headset interface NOT FOUND"); return; }
        using (HidDevice dev = HidDevice.Open(h))
        {
            Console.WriteLine("opened headset, outLen=" + h.OutputReportByteLength +
                              " inLen=" + h.InputReportByteLength);
            Console.WriteLine("drain -> " + dev.Drain(16, 5));

            byte[] wire = Window(0x27, 0x00, new byte[] { 0x20, 0x00, 0xFF }, 0x03);
            byte[] buf65 = new byte[65];
            Array.Copy(wire, 0, buf65, 1, 64);
            Console.WriteLine("SetOutputReport(65) -> " + dev.SetOutputReport(buf65, 65));
            Thread.Sleep(120);
            byte[] ack = new byte[128];
            int n = dev.Read(ack, 300);
            Console.WriteLine("ack (" + n + "): " + Hex(ack, Math.Min(n, 24)));
            Console.WriteLine("drain -> " + dev.Drain(16, 10));

            // status query 0x07, then drain replies
            byte[] q = new byte[65];
            q[0] = 0x00; q[1] = 0xAA; q[2] = 0x07;
            Console.WriteLine("query 0x07 -> " + dev.SetOutputReport(q, 65));
            Thread.Sleep(150);
            for (int i = 0; i < 3; i++)
            {
                n = dev.Read(ack, 250);
                if (n <= 0) break;
                Console.WriteLine("query reply " + i + " (" + n + "): " + Hex(ack, Math.Min(n, 24)));
                if (n > 14) Console.WriteLine("   rgb@12..14 = " + ack[12] + "," + ack[13] + "," + ack[14]);
            }

            // what does WriteFile think of the 65-byte buffer?
            Console.WriteLine("WriteFile(65) -> " + dev.Write(buf65, 65));
            Thread.Sleep(80);
            Console.WriteLine("drain -> " + dev.Drain(16, 10));
        }

        Console.WriteLine("done");
    }

    private static byte[] Window(byte cmd, byte mode, byte[] rgb, byte bright)
    {
        byte[] p = new byte[64];
        p[0] = 0xAA;
        p[1] = cmd;
        p[5] = 0x0A;
        p[6] = 0x01;
        p[7] = mode;
        p[8] = 0x05;
        p[9] = 0x00;
        p[10] = bright;
        p[12] = rgb[0]; p[13] = rgb[1]; p[14] = rgb[2];
        return p;
    }

    private static byte[] Pkt(byte cmd, byte param, byte[] payload)
    {
        byte[] p = new byte[64];
        p[0] = 0x09;
        p[1] = cmd;
        p[2] = param;
        p[4] = 1;
        p[5] = 0;
        if (payload != null)
        {
            Array.Copy(payload, 0, p, 7, Math.Min(payload.Length, 56));
            p[6] = (byte)Math.Min(payload.Length, 56);
        }
        int sum = 0;
        for (int i = 0; i < 63; i++) sum += p[i];
        p[63] = (byte)((0xFF - (sum & 0xFF)) & 0xFF);
        return p;
    }
}
