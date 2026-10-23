# -*- coding: utf-8 -*-
"""
Double-click launcher.

1. re-launches itself elevated if needed (the laptop backlight is driven through
   Clevo WMI, which refuses non-admin callers)
2. starts the local UI server on a free port
3. opens it in a chromeless browser window, so it reads as a desktop app rather
   than a web page
"""
import ctypes
import os
import socket
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

APP_TITLE = "神光同步"


def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def relaunch_elevated() -> bool:
    """Returns True if a new elevated instance was started."""
    if is_admin():
        return False
    exe = sys.executable
    # prefer the windowless interpreter so no console flashes up
    cand = os.path.join(os.path.dirname(exe), "pythonw.exe")
    if os.path.exists(cand):
        exe = cand
    try:
        rc = ctypes.windll.shell32.ShellExecuteW(
            None, "runas", exe, '"%s"' % os.path.abspath(__file__), ROOT, 1)
        return rc > 32
    except Exception:
        return False


def free_port(start=8760, tries=40) -> int:
    for p in range(start, start + tries):
        with socket.socket() as s:
            if s.connect_ex(("127.0.0.1", p)) != 0:
                return p
    return start


BROWSERS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
]


def find_app_pid(url: str, timeout: float = 8.0):
    """
    PID of the browser process that actually shows our window.

    Edge/Chrome fork when started with --app, so the Popen handle we get exits
    within a second while the window lives on. The reliable identity is the
    process whose command line carries our URL.
    """
    ps = ("Get-CimInstance Win32_Process | Where-Object { "
          "$_.CommandLine -like '*--app=%s*' } | "
          "Select-Object -ExpandProperty ProcessId" % url)
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                               capture_output=True, timeout=20,
                               creationflags=0x08000000)
            txt = (r.stdout or b"").decode("utf-8", "replace")
            for line in txt.splitlines():
                line = line.strip()
                if line.isdigit():
                    return int(line)
        except Exception:
            pass
        time.sleep(0.8)
    return None


STILL_ACTIVE = 259


def alive(pid: int) -> bool:
    """True while the process still runs. Works across elevation boundaries."""
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    k32 = ctypes.windll.kernel32
    h = k32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not h:
        return False
    try:
        code = ctypes.c_ulong()
        if not k32.GetExitCodeProcess(h, ctypes.byref(code)):
            return False
        return code.value == STILL_ACTIVE
    finally:
        k32.CloseHandle(h)


def open_window(url: str):
    """
    Open a chromeless window (no address bar) so it looks like an app.

    Returns the Popen handle when we launched the browser ourselves, so the
    caller can watch it and shut down the moment the user closes the window.
    """
    for exe in BROWSERS:
        if os.path.exists(exe):
            try:
                return subprocess.Popen([exe, "--app=%s" % url,
                                         "--window-size=1020,900",
                                         "--window-position=120,60"])
            except Exception:
                continue
    # Fallback: Explorer is already running as the logged-on user, so it hands
    # the URL to a non-elevated browser. Chrome/Edge refuse to start with an
    # administrator token, which is what this process holds.
    try:
        subprocess.Popen(["explorer.exe", url])
    except Exception:
        os.startfile(url)      # last resort
    return None


def kill_old_instances() -> None:
    """
    Stop any server already holding one of our ports.

    Without this, every double-click leaves the previous instance running and
    they all fight over the same HID devices. Only works when elevated, which is
    the normal case here.
    """
    ps = ("Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | "
          "Where-Object { $_.LocalPort -ge 8760 -and $_.LocalPort -le 8799 } | "
          "ForEach-Object { try { Stop-Process -Id $_.OwningProcess -Force "
          "-ErrorAction Stop; 'stopped ' + $_.OwningProcess } catch { } }")
    try:
        subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                       capture_output=True, timeout=25,
                       creationflags=0x08000000)
    except Exception:
        pass


def main():
    args = sys.argv[1:]
    no_elevate = "--no-elevate" in args
    no_browser = "--no-browser" in args
    forced_port = 0
    if "--port" in args:
        try:
            forced_port = int(args[args.index("--port") + 1])
        except (IndexError, ValueError):
            forced_port = 0

    if not no_elevate and relaunch_elevated():
        return

    kill_old_instances()          # never stack instances
    time.sleep(0.6)

    port = forced_port or free_port()
    url = "http://127.0.0.1:%d/" % port

    from gui.server import STATE, Handler
    from http.server import ThreadingHTTPServer

    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True, name="ui").start()
    time.sleep(0.25)
    proc = None if no_browser else open_window(url)
    print("%s -> %s" % (APP_TITLE, url), flush=True)

    # Exit when the window goes away. Two signals only, both measured to work:
    #   1. the page sent /api/bye on unload  -> immediate, normal close
    #   2. no request for IDLE_LIMIT seconds -> fallback, covers a crash/kill
    #
    # Deliberately NOT watching the browser process: Edge/Chrome fork when
    # started with --app, so the handle we get exits within a second while the
    # window is still open. That bogus signal shut the server down instantly.
    started = time.time()
    IDLE_LIMIT = 12.0
    try:
        while True:
            time.sleep(0.8)
            if STATE.closed:
                print("page said bye - shutting down", flush=True)
                break
            if time.time() - started > 8 and \
                    time.time() - STATE.last_seen > IDLE_LIMIT:
                print("no client for %.0fs - shutting down" % IDLE_LIMIT, flush=True)
                break
    except KeyboardInterrupt:
        pass
    finally:
        STATE.stop()
        time.sleep(1.5)          # let the engine loop close the adapters
        srv.shutdown()
        print("released", flush=True)


if __name__ == "__main__":
    main()
