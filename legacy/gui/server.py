# -*- coding: utf-8 -*-
"""
Local control surface for the RGB sync engine.

Serves a single-page UI and a tiny JSON API over http://127.0.0.1:<port>.
Standard library only - the UI is plain HTML/CSS/JS, so there is nothing to
install and nothing to keep up to date.

    python -m gui.server              # start and open the UI
    python -m gui.server --no-open    # start only (for debugging)

The engine runs on a background thread. Switching effects does not restart it -
``SyncEngine.set_effect`` swaps the effect under the running loop, so the
devices never blink out.
"""
import argparse
import ctypes
import json
import os
import subprocess
import sys
import threading
import time
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from rgbsync import effects as effect_registry          # noqa: E402
from rgbsync.core import SyncEngine, TrioFlow          # noqa: E402

UI_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui")

# where each device sits on the desk, mirrored here so the preview can be drawn
# even when a device is unplugged
DEVICE_META = [
    {"id": "headset", "label": "耳机", "sub": "MCHOSE V9 Turbo+",
     "span": (0.0, 0.13), "single": True},
    {"id": "laptop", "label": "笔记本", "sub": "X15 AT23 键盘",
     "span": (0.21, 0.53), "single": True},
    {"id": "keyboard", "label": "键盘", "sub": "HERO 68 HE",
     "span": (0.60, 1.0), "single": False},
]


def is_admin() -> bool:
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def kill_mchose() -> str:
    """Stop M HUB and its watchdog. It re-pushes its own lighting every ~200 ms."""
    ps = ("Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'MCHOSE' } | "
          "ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop;"
          " 'killed ' + $_.Name } catch { 'fail ' + $_.Name } }")
    try:
        r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                           capture_output=True, timeout=40,
                           creationflags=0x08000000)   # CREATE_NO_WINDOW
        out = (r.stdout or b"").decode("utf-8", "replace").strip()
        return out or "M HUB 未在运行"
    except Exception as exc:
        return "关闭失败: %s" % exc


# HERO 68 HE is a 65% board with 67 addressable keys. Each row lists relative
# key widths so the preview looks like a keyboard (long space bar, wide shift).
# Key counts must add up to the real 67: 14 + 14 + 13 + 12 + 14.
KEY_ROWS = [
    [1] * 14,                                        # esc + F-row
    [1.5] + [1] * 12 + [1.5],                        # tab ... backslash
    [1.75] + [1] * 11 + [2.25],                      # caps ... enter
    [2.25] + [1] * 10 + [2.75],                      # shift ... shift
    [1.25, 1.25, 1.25, 6.25, 1.25, 1.25, 1.25, 1, 1, 1, 1, 1, 1, 1],
]

_KBD_XS = None


def keyboard_xs():
    """
    The 67 horizontal positions AULA itself defines for this board, ascending.

    Using the vendor table (instead of an even spread) is what keeps the preview
    honest: the same numbers feed the hardware adapter, so what you see is what
    the keys actually do.
    """
    global _KBD_XS
    if _KBD_XS is None:
        try:
            from rgbsync.adapters.hero68 import Hero68Adapter
            a = Hero68Adapter()
            a._uid = "110000000003"
            a._load_vendor_layout()
            _KBD_XS = sorted(a.global_positions().values())
        except Exception:
            _KBD_XS = []
    return _KBD_XS


class AppState:
    """Everything the UI can see, guarded by one lock."""

    def __init__(self):
        self.lock = threading.Lock()
        self.engine = None
        self.thread = None
        self.running = False
        self.effect_id = effect_registry.DEFAULT_EFFECT
        self.speed = 1.0
        self.power = 1.0
        self.color = (255, 0, 0)
        self.fps = 30
        self.uniform = False
        self.alive = {}          # device id -> bool
        self.notes = []          # recent log lines for the UI
        self.started_at = 0.0
        # M HUB re-pushes its own lighting every ~200 ms, so leaving it running
        # fights with us for the same device. Kill it on the first start.
        self.auto_kill_hub = True
        self._hub_checked = False
        # liveness for the shutdown watchdog: closing the window must release
        # the devices, so the launcher watches these two fields
        self.last_seen = time.time()
        self.closed = False

    # -- helpers --------------------------------------------------------
    def note(self, text: str) -> None:
        # plain append: the GIL makes this atomic and, unlike taking self.lock,
        # it is safe to call from inside methods that already hold the lock
        self.notes.append("%s  %s" % (time.strftime("%H:%M:%S"), text))
        if len(self.notes) > 40:
            self.notes = self.notes[-40:]

    def make_effect(self):
        return effect_registry.build(self.effect_id, self.speed, self.power,
                                     self.color)

    # -- engine lifecycle ------------------------------------------------
    def start(self):
        with self.lock:
            if self.running:
                # live effect swap, no restart
                self.engine.set_effect(self.make_effect(), reset_phase=True)
                self.note("切换效果 -> %s" % self.effect_id)
                return
        engine = SyncEngine(self.make_effect(), fps=self.fps)
        engine.uniform = self.uniform

        if self.auto_kill_hub and not self._hub_checked:
            self._hub_checked = True
            # off the request thread: spawning PowerShell can take a second and
            # the UI should not wait for it
            def _killer():
                out = kill_mchose().replace("\n", " | ")
                self.note("自动关闭 M HUB: %s" % (out[:70] or "未在运行"))
            threading.Thread(target=_killer, daemon=True, name="kill-hub").start()

        for ad in self._make_adapters():
            engine.add(ad)
        with self.lock:
            self.engine = engine
            self.running = True
            self.started_at = time.time()
        self.note("启动: 效果=%s fps=%d" % (self.effect_id, self.fps))

        def _loop():
            try:
                engine.run(0)
            except Exception as exc:
                self.note("引擎异常: %s" % exc)
            finally:
                with self.lock:
                    self.running = False
                self.note("引擎已停止")

        self.thread = threading.Thread(target=_loop, daemon=True,
                                       name="rgb-engine")
        self.thread.start()

    def _make_adapters(self):
        out = []
        try:
            from rgbsync.adapters.clevo import ClevoAdapter
            out.append(ClevoAdapter())
        except Exception as exc:
            self.note("笔记本适配器不可用: %s" % exc)
        try:
            from rgbsync.adapters.hero68 import Hero68Adapter
            out.append(Hero68Adapter())
        except Exception as exc:
            self.note("键盘适配器不可用: %s" % exc)
        try:
            from rgbsync.adapters.mchose import MchoseV9Adapter
            out.append(MchoseV9Adapter())
        except Exception as exc:
            self.note("耳机适配器不可用: %s" % exc)
        return out

    def stop(self):
        with self.lock:
            engine = self.engine
        if engine:
            engine.stop()
        self.note("请求停止")

    # -- preview ---------------------------------------------------------
    def preview(self):
        """Colours for the UI strip, computed from the current effect."""
        eff = self.engine.effect if (self.engine and self.running) else self.make_effect()
        t = (time.perf_counter() - self.engine._t0) if (self.engine and self.running) else 0.0
        out = []
        for dev in DEVICE_META:
            a, b = dev["span"]
            if dev["single"]:
                rgb = eff.zone_color((a + b) / 2.0, t)
                out.append({"id": dev["id"], "rgb": rgb, "steps": [rgb]})
            else:
                # colours follow the vendor's real key positions, laid out over
                # the row shape so it reads as a keyboard
                xs = keyboard_xs()
                if not xs:
                    xs = [a + (b - a) * ((i + 0.5) / 67.0) for i in range(67)]
                rows = []
                idx = 0
                for widths in KEY_ROWS:
                    keys = []
                    for w in widths:
                        x = xs[min(idx, len(xs) - 1)]
                        keys.append({"rgb": eff.color_at(x, t), "w": round(w, 2)})
                        idx += 1
                    rows.append({"keys": keys})
                flat = [k["rgb"] for r in rows for k in r["keys"]]
                out.append({"id": dev["id"], "rgb": flat[len(flat) // 2],
                            "steps": flat, "rows": rows})
        return out

    def snapshot(self):
        with self.lock:
            running = self.running
            effect_id = self.effect_id
            speed = self.speed
            power = self.power
            notes = list(self.notes)
            uptime = (time.time() - self.started_at) if running else 0.0
            fps = getattr(self.engine, "_frames", 0) if self.engine else 0
        alive = {}
        if self.engine:
            for ad in list(self.engine.adapters):
                alive[ad.name] = True
        return {
            "running": running,
            "effect": effect_id,
            "speed": speed,
            "power": power,
            "uptime": round(uptime, 1),
            "admin": is_admin(),
            "devices": [dict(d, alive=alive.get(
                {"headset": "headset", "laptop": "laptop",
                 "keyboard": "hero68"}[d["id"]], False)) for d in DEVICE_META],
            "preview": self.preview(),
            "notes": notes,
        }


STATE = AppState()


def _safe(fn):
    """Never let a handler exception kill the connection: answer 500 instead."""
    def wrapper(self, *a, **kw):
        STATE.last_seen = time.time()      # window is alive
        try:
            return fn(self, *a, **kw)
        except Exception as exc:
            import traceback
            traceback.print_exc()
            try:
                self._json({"ok": False, "error": "%s: %s" % (type(exc).__name__, exc)}, 500)
            except Exception:
                pass
    return wrapper


class Handler(BaseHTTPRequestHandler):
    server_version = "RGBSync/1.0"

    def log_message(self, fmt, *args):
        pass                                   # keep the console clean

    # -- plumbing --------------------------------------------------------
    def _send(self, code, body, ctype="application/json; charset=utf-8"):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        try:
            self.wfile.write(body)
        except Exception:
            pass

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj, ensure_ascii=False))

    def _body(self):
        try:
            n = int(self.headers.get("Content-Length") or 0)
            if not n:
                return {}
            return json.loads(self.rfile.read(n).decode("utf-8"))
        except Exception:
            return {}

    # -- routes ----------------------------------------------------------
    @_safe
    def do_GET(self):
        path = self.path.split("?")[0]
        if path in ("/", "/index.html"):
            f = os.path.join(UI_DIR, "index.html")
            if not os.path.exists(f):
                return self._send(500, "UI missing: %s" % f, "text/plain; charset=utf-8")
            with open(f, "rb") as fh:
                return self._send(200, fh.read(), "text/html; charset=utf-8")
        if path == "/api/state":
            return self._json(STATE.snapshot())
        if path == "/api/catalog":
            return self._json({"effects": effect_registry.catalog(),
                               "default": effect_registry.DEFAULT_EFFECT})
        return self._send(404, "not found", "text/plain; charset=utf-8")

    @_safe
    def do_POST(self):
        path = self.path.split("?")[0]
        body = self._body()

        if path == "/api/start":
            with STATE.lock:
                if body.get("effect"):
                    STATE.effect_id = body["effect"]
                if "speed" in body:
                    STATE.speed = float(body["speed"])
                if "power" in body:
                    STATE.power = float(body["power"])
                if "uniform" in body:
                    STATE.uniform = bool(body["uniform"])
            STATE.start()
            return self._json({"ok": True, "state": STATE.snapshot()})

        if path == "/api/stop":
            STATE.stop()
            return self._json({"ok": True})

        if path == "/api/bye":
            # sent by the page on unload: release the devices right away
            STATE.closed = True
            return self._json({"ok": True})

        if path == "/api/killhub":
            out = kill_mchose()
            STATE.note("关闭 M HUB: %s" % out.replace("\n", " | "))
            return self._json({"ok": True, "detail": out})

        if path == "/api/settings":
            with STATE.lock:
                if body.get("effect"):
                    STATE.effect_id = body["effect"]
                if "speed" in body:
                    STATE.speed = float(body["speed"])
                if "power" in body:
                    STATE.power = float(body["power"])
            if STATE.running and STATE.engine:
                STATE.engine.set_effect(STATE.make_effect())
            return self._json({"ok": True})

        return self._send(404, "not found", "text/plain; charset=utf-8")


def pick_port(start=8760, tries=30):
    import socket
    for p in range(start, start + tries):
        with socket.socket() as s:
            if s.connect_ex(("127.0.0.1", p)) != 0:
                return p
    return start


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=0)
    ap.add_argument("--no-open", action="store_true")
    args = ap.parse_args()

    port = args.port or pick_port()
    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    url = "http://127.0.0.1:%d/" % port
    print("RGB sync UI -> %s" % url, flush=True)
    print("admin=%s" % is_admin(), flush=True)
    if not args.no_open:
        threading.Timer(0.4, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nbye")
    finally:
        STATE.stop()
        srv.server_close()


if __name__ == "__main__":
    main()
