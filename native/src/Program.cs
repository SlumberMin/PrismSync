// PrismSync native app - process entry: single instance, elevation, tray lifecycle.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using PrismSync.UI;

namespace PrismSync
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static EventWaitHandle _showEvent;

        [STAThread]
        private static void Main(string[] args)
        {
            bool noElevate = HasArg(args, "--no-elevate");
            bool elevatedLaunch = HasArg(args, "--elevated");

            // The normal run is elevated (laptop WMI) but a second double-click can be
            // non-elevated. Default kernel-object ACLs would then block the second
            // instance from finding the first one - and two engines would fight over
            // the devices, which looks exactly like flickering. Create the mutex and
            // the "show" event with an explicit world-accessible ACL.
            bool created;
            try
            {
                _mutex = CreateInstanceMutex(out created);
            }
            catch (UnauthorizedAccessException)
            {
                SignalExisting();
                return;
            }
            if (!created)
            {
                SignalExisting();
                return;
            }

            Settings settings = Settings.Load();
            bool admin = AdminCheck.IsAdmin();
            string joinedArgs = string.Join(" ", args);
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDir, "startup.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  args=[" + joinedArgs + "]  admin=" +
                    admin + "  elevatedLaunch=" + elevatedLaunch + Environment.NewLine);
            }
            catch { }
            if (!admin && !noElevate && !elevatedLaunch && settings.AutoElevate && settings.LaptopEnabled)
            {
                if (TryElevate())
                {
                    try
                    {
                        System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDir, "startup.log"),
                            "    -> relaunched elevated, this instance exits" + Environment.NewLine);
                    }
                    catch { }
                    GC.KeepAlive(_mutex);
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) LogCrash(ex);
            };

            AppController app = new AppController(admin, HasArg(args, "--no-devices"));
            string debugEffect = ArgValue(args, "--effect");
            if (!string.IsNullOrEmpty(debugEffect)) app.SetEffect(debugEffect);
            MainForm form = new MainForm(app);
            TrayController tray = new TrayController(form, app);
            app.QuitRequested += delegate
            {
                try { tray.Dispose(); } catch { }
                try { form.ForceClose(); } catch { }
                Application.Exit();
            };

            _showEvent = CreateShowEvent();
            Thread waiter = new Thread(delegate ()
            {
                while (true)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        form.BeginInvoke(new MethodInvoker(form.ShowFromTray));
                    }
                    catch { return; }
                }
            });
            waiter.IsBackground = true;
            waiter.Name = "show-signal";
            waiter.Start();

            if (settings.StartRunning) app.Start();

            try
            {
                Application.Run(form);
            }
            finally
            {
                try { app.Quit(); } catch { }
                try { tray.Dispose(); } catch { }
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static Mutex CreateInstanceMutex(out bool created)
        {
            MutexSecurity security = new MutexSecurity();
            SecurityIdentifier world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            security.AddAccessRule(new MutexAccessRule(world,
                MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
            return new Mutex(true, @"Local\PrismSync.App", out created, security);
        }

        private static EventWaitHandle CreateShowEvent()
        {
            bool created;
            EventWaitHandleSecurity security = new EventWaitHandleSecurity();
            SecurityIdentifier world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            security.AddAccessRule(new EventWaitHandleAccessRule(world,
                EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                AccessControlType.Allow));
            return new EventWaitHandle(false, EventResetMode.AutoReset,
                @"Local\PrismSync.Show", out created, security);
        }

        private static void SignalExisting()
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(@"Local\PrismSync.Show"))
                {
                    ev.Set();
                    return;
                }
            }
            catch { }

            // Fallback: show the existing window directly.
            IntPtr h = FindWindow(null, "棱镜同步 PrismSync");
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, 9 /* SW_RESTORE */);
                SetForegroundWindow(h);
            }
        }

        private static bool TryElevate()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                IntPtr r = ShellExecuteW(IntPtr.Zero, "runas", exe, "\"--elevated\"", null, 1);
                return (long)r > 32;
            }
            catch { return false; }
        }

        private static void LogCrash(Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(AppPaths.DataDir, "crash.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine);
            }
            catch { }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ShellExecuteW(IntPtr hwnd, string verb, string file,
            string parameters, string directory, int showCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
