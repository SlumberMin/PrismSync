// audio_test.cs - standalone test for native/src/Capture/AudioMeter.cs.
//
// Opens the default microphone, prints 3 seconds of level statistics, then tests
// Stop/Start re-entrancy and double-Stop. Never installs anything and never hangs
// when there is no microphone: an unavailable device returns exit code 2.
//
// Build (from the repo root):
//   csc /nologo /target:exe /platform:x64 /codepage:65001 /out:%TEMP%\audio_test.exe ^
//       native\tools\audio_test.cs native\src\Capture\AudioMeter.cs
using System;
using System.Threading;
using PrismSync;

internal static class AudioTest
{
    private static int Main()
    {
        int exit = 0;

        using (AudioMeter meter = new AudioMeter())
        {
            bool started = meter.Start();
            Console.WriteLine("[audio] Start()=" + started + " Available=" + meter.Available + " LastError=" + Text(meter.LastError));
            if (!meter.Available)
            {
                Console.WriteLine("[audio] RESULT NO_MIC (优雅不可用，未阻塞)");
                return 2;
            }

            Console.WriteLine("[audio] device=\"" + Text(meter.DeviceName) + "\" id=" + meter.DeviceId +
                              " rate=" + AudioMeter.SampleRate + " Hz buffer=" + AudioMeter.BufferMilliseconds + " ms");

            const int samples = 60;                      // 60 x 50 ms = 3 s
            float minLevel = 1f, maxLevel = 0f, sumLevel = 0f;
            float maxBass = 0f, maxMid = 0f, maxTreble = 0f, maxPeak = 0f;
            int beatFrames = 0;

            for (int i = 0; i < samples; i++)
            {
                Thread.Sleep(50);
                float level = meter.Level;
                sumLevel += level;
                if (level < minLevel) minLevel = level;
                if (level > maxLevel) maxLevel = level;
                if (meter.Bass > maxBass) maxBass = meter.Bass;
                if (meter.Mid > maxMid) maxMid = meter.Mid;
                if (meter.Treble > maxTreble) maxTreble = meter.Treble;
                if (meter.Peak > maxPeak) maxPeak = meter.Peak;
                if (meter.Beat) beatFrames++;

                if ((i + 1) % 10 == 0)
                {
                    Console.WriteLine(string.Format(
                        "[audio] t={0,4:F1}s level={1:F3} bass={2:F3} mid={3:F3} treble={4:F3} peak={5:F3} beat={6} samples={7}",
                        (i + 1) * 0.05, level, meter.Bass, meter.Mid, meter.Treble, meter.Peak,
                        meter.Beat ? 1 : 0, meter.SampleCount));
                }
            }

            float avgLevel = sumLevel / samples;
            Console.WriteLine("[audio] 3s stats: level avg=" + avgLevel.ToString("F3") +
                              " min=" + minLevel.ToString("F3") + " max=" + maxLevel.ToString("F3"));
            Console.WriteLine("[audio] 3s bands: bass max=" + maxBass.ToString("F3") +
                              " mid max=" + maxMid.ToString("F3") +
                              " treble max=" + maxTreble.ToString("F3") +
                              " peak max=" + maxPeak.ToString("F3"));
            Console.WriteLine("[audio] samples=" + meter.SampleCount + " beatFrames=" + beatFrames +
                              " beatCount=" + meter.BeatCount);
            if (meter.SampleCount <= 0)
            {
                Console.WriteLine("[audio] FAIL microphone opened but delivered no samples");
                Console.WriteLine("[audio] LastError=" + Text(meter.LastError));
                exit = 3;
            }

            // ------------------------------------------------------------- stop/start
            meter.Stop();
            Console.WriteLine("[audio] after Stop(): Available=" + meter.Available + " Level=" + meter.Level +
                              " IsRunning=" + meter.IsRunning);
            if (meter.Available || meter.Level != 0f)
            {
                Console.WriteLine("[audio] FAIL Stop() did not reset state");
                exit = 3;
            }

            bool again1 = meter.Start();
            bool again2 = meter.Start();                 // must be idempotent
            Thread.Sleep(400);
            Console.WriteLine("[audio] restart: start=" + again1 + " start-again=" + again2 +
                              " Available=" + meter.Available + " samples=" + meter.SampleCount +
                              " LastError=" + Text(meter.LastError));
            if (!again1 || !again2 || !meter.Available || meter.SampleCount <= 0)
            {
                Console.WriteLine("[audio] FAIL re-entrant Start() did not resume capture");
                exit = 3;
            }

            meter.Stop();
            meter.Stop();                                // double Stop must be harmless
            Console.WriteLine("[audio] double Stop() ok, Available=" + meter.Available + " IsRunning=" + meter.IsRunning);
        }

        Console.WriteLine(exit == 0 ? "[audio] RESULT PASS" : "[audio] RESULT FAIL code=" + exit);
        return exit;
    }

    private static string Text(string s)
    {
        return s == null ? "-" : s;
    }
}
