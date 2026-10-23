# -*- coding: utf-8 -*-
"""
WebHID capture, hardened version.

- auto-restarts the browser if it gets closed (so a stray click does not kill
  the session)
- tries to click straight into "M HUB WEB" / driver entries
- prints the page's real links so the correct driver URL can be identified
- timestamps every captured transaction

Usage:
    python webhid_capture_v2.py <target_url> <duration_seconds> [log_file]
"""
import asyncio
import json
import sys
import time

from playwright.async_api import async_playwright

LOG = r"d:\Users\Lie\Desktop\神光同步\_webhid_capture.jsonl"

HOOK = r"""
(() => {
  const log = (obj) => console.log("WBHOOK " + JSON.stringify(obj));
  const hex = (buf) => {
    try {
      const u8 = new Uint8Array(buf.buffer ? buf.buffer : buf, buf.byteOffset || 0, buf.byteLength !== undefined ? buf.byteLength : buf.length);
      return Array.from(u8).map(x => x.toString(16).padStart(2, "0")).join(" ");
    } catch (e) { return String(buf); }
  };
  if (!navigator.hid) { log({t: "no_navigator_hid"}); return; }
  const origRequest = navigator.hid.requestDevice.bind(navigator.hid);
  navigator.hid.requestDevice = async (opts) => {
    log({t: "requestDevice", filters: opts && opts.filters});
    const devs = await origRequest(opts);
    log({t: "requestDevice_result", count: devs.length, names: devs.map(d => d.productName)});
    for (const d of devs) {
      log({t: "device_opened", name: d.productName, vid: d.vendorId, pid: d.productId});
      const wrap = (name, fn) => {
        if (!fn) return null;
        return async (...args) => {
          const res = await fn(...args);
          log({t: name, device: d.productName, args: args.map(a => (a && a.byteLength !== undefined) ? hex(a) : a), ret: (res && res.byteLength !== undefined) ? hex(res) : res});
          return res;
        };
      };
      d.sendReport = wrap("sendReport", d.sendReport && d.sendReport.bind(d)) || d.sendReport;
      d.sendFeatureReport = wrap("sendFeatureReport", d.sendFeatureReport && d.sendFeatureReport.bind(d)) || d.sendFeatureReport;
      d.receiveFeatureReport = wrap("receiveFeatureReport", d.receiveFeatureReport && d.receiveFeatureReport.bind(d)) || d.receiveFeatureReport;
    }
    return devs;
  };
  log({t: "hooked"});
})();
"""


def log_line(obj) -> None:
    obj = dict(obj)
    obj["ts"] = round(time.time(), 3)
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps(obj, ensure_ascii=False) + "\n")


async def try_click_driver(page) -> None:
    """Best effort: jump straight into the web driver if a known entry exists."""
    candidates = [
        "text=进入 M HUB WEB", "text=M HUB WEB", "text=进入M HUB WEB",
        "text=网页版驱动", "text=进入驱动", "text=在线驱动",
        "text=WEB DRIVER", "text=Web Driver",
    ]
    for sel in candidates:
        try:
            el = page.locator(sel).first
            if await el.count() > 0 and await el.is_visible():
                print(">>> clicking entry:", sel)
                await el.click(timeout=5000)
                await page.wait_for_timeout(4000)
                return
        except Exception:
            continue
    print(">>> no known driver entry found; listing links instead")


async def dump_links(page) -> None:
    try:
        links = await page.eval_on_selector_all(
            "a[href]", "els => els.map(e => [e.innerText.trim().slice(0,40), e.href]).filter(x => x[1])"
        )
        print(">>> page links:")
        for text, href in links[:40]:
            print("    %-40s %s" % (text, href))
    except Exception as exc:
        print("link dump failed:", exc)


async def run_session(p, url: str, duration: int) -> bool:
    browser = await p.chromium.launch(
        channel="chrome", headless=False,
        args=["--start-maximized", "--disable-popup-blocking"],
    )
    try:
        ctx = await browser.new_context(no_viewport=True)
        page = await ctx.new_page()

        def on_console(msg):
            txt = msg.text
            if txt.startswith("WBHOOK "):
                log_line(json.loads(txt[7:]))
                if '"device_opened"' in txt:
                    print(">>> DEVICE CONNECTED - now change the lighting!", flush=True)

        page.on("console", on_console)
        await page.add_init_script(HOOK)
        await page.goto(url, wait_until="domcontentloaded", timeout=60000)
        log_line({"t": "page_loaded", "url": url})
        print(">>> page loaded:", url, flush=True)

        await try_click_driver(page)
        await dump_links(page)

        await page.wait_for_timeout(duration * 1000)
        return True
    finally:
        try:
            await browser.close()
        except Exception:
            pass


async def main() -> None:
    global LOG
    url = sys.argv[1]
    duration = int(sys.argv[2]) if len(sys.argv) > 2 else 300
    if len(sys.argv) > 3:
        LOG = sys.argv[3]

    async with async_playwright() as p:
        attempt = 0
        while attempt < 6:
            attempt += 1
            print("=== session attempt %d (url=%s, window=%ds) ===" % (attempt, url, duration), flush=True)
            try:
                await run_session(p, url, duration)
                break
            except Exception as exc:
                print(">>> session ended: %s -- restarting in 2s" % exc, flush=True)
                await asyncio.sleep(2)


asyncio.run(main())
