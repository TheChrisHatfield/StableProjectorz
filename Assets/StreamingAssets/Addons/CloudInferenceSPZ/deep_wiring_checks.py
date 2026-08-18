"""Deep wiring/regression checks for CloudInferenceSPZ (target ~50 assertions)."""

from __future__ import annotations

import json
import os
import sys
import threading
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler

_root = os.path.dirname(os.path.abspath(__file__))
if _root not in sys.path:
    sys.path.insert(0, _root)

import backends as be
import forge_shim as shim

CHECKS = 0
FAILS = 0


def check(cond: bool, msg: str) -> None:
    global CHECKS, FAILS
    CHECKS += 1
    if cond:
        print(f"  OK {CHECKS}: {msg}")
    else:
        FAILS += 1
        print(f"FAIL {CHECKS}: {msg}")


def http(method: str, base: str, path: str, payload=None, timeout=30):
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(f"{base}{path}", data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            try:
                body = json.loads(raw.decode("utf-8")) if raw else None
            except Exception:
                body = raw
            return int(resp.status), body
    except urllib.error.HTTPError as exc:
        raw = exc.read()
        try:
            body = json.loads(raw.decode("utf-8")) if raw else None
        except Exception:
            body = raw
        return int(exc.code), body


def main() -> int:
    host, port = "127.0.0.1", 7860
    if not shim.is_port_free(host, port):
        port = 17861
        print(f"[deep] :7860 busy — using {port}")

    shim.get_state().set_backend(be.DemoBackend())
    ok, msg = shim.start_shim(host, port)
    check(ok, f"start_shim: {msg}")
    time.sleep(0.15)
    base = f"http://{host}:{port}"

    try:
        st, body = http("GET", base, "/internal/ping")
        check(st == 200 and isinstance(body, dict) and body.get("status") == "ok", "ping")
        ping_req = urllib.request.Request(f"{base}/internal/ping", method="GET")
        with urllib.request.urlopen(ping_req, timeout=5) as ping_resp:
            conn_hdr = (ping_resp.headers.get("Connection") or "").lower()
            check("close" in conn_hdr, "ping Connection: close (no keep-alive stall)")

        st, body = http("GET", base, "/internal/sysinfo")
        check(st == 200 and isinstance(body, dict), "sysinfo 200")
        check(bool(body.get("Data path")), "sysinfo Data path key")
        check(bool(body.get("Script path")), "sysinfo Script path key")
        check(str(body.get("Version", "")).lower().startswith("neo"), "sysinfo Version neo*")
        check("forge" in str(body.get("Data path", "")).lower(), "sysinfo forge-family path")
        check(isinstance(body.get("Config"), dict), "sysinfo Config object")
        check(int(body["Config"].get("control_net_unit_count", 0)) >= 1, "sysinfo CN unit count")

        for path, key in (
            ("/sdapi/v1/sd-models", "model_name"),
            ("/sdapi/v1/samplers", "name"),
            ("/sdapi/v1/schedulers", "name"),
            ("/sdapi/v1/upscalers", "name"),
            ("/sdapi/v1/sd-vae", "model_name"),
            ("/sdapi/v1/sd-modules", "model_name"),
        ):
            st, body = http("GET", base, path)
            check(st == 200 and isinstance(body, list) and len(body) > 0, f"GET {path} list")
            check(isinstance(body[0], dict) and key in body[0], f"GET {path} item.{key}")

        st, body = http("GET", base, "/sdapi/v1/options")
        check(st == 200 and "sd_model_checkpoint" in body, "options get")
        st, body = http("POST", base, "/sdapi/v1/options", {"sd_model_checkpoint": "cloud-inference-demo"})
        check(st == 200 and body.get("sd_model_checkpoint") == "cloud-inference-demo", "options post")

        st, body = http("GET", base, "/controlnet/model_list")
        check(st == 200 and "model_list" in body, "controlnet model_list")
        st, body = http("GET", base, "/controlnet/module_list")
        check(st == 200 and "module_list" in body, "controlnet module_list")
        st, body = http("POST", base, "/controlnet/detect", {"controlnet_module": "none", "controlnet_input_images": []})
        check(st == 200 and "images" in body, "controlnet detect stub")
        st, body = http(
            "POST",
            base,
            "/controlnet/detect",
            {"controlnet_module": "none", "controlnet_input_images": ["QUJD"]},
        )
        check(st == 200 and body.get("images") == ["QUJD"], "controlnet detect echoes input images")

        st, body = http("POST", base, "/sdapi/v1/txt2img", {"width": 32, "height": 32, "prompt": "a"})
        check(st == 200 and body.get("images") and len(body["images"][0]) > 16, "txt2img")
        st, body = http("POST", base, "/sdapi/v1/img2img", {"width": 32, "height": 32, "init_images": []})
        check(st == 200 and body.get("images"), "img2img")
        st, body = http("POST", base, "/sdapi/v1/extra-batch-images", {"resize_width": 48, "resize_height": 48})
        check(st == 200 and body.get("images"), "extra-batch-images")
        st, body = http(
            "POST",
            base,
            "/sdapi/v1/extra-batch-images",
            {"resize_width": 32.0, "resize_height": "32", "rslt_imageWidths": 96, "rslt_imageHeights": 96},
        )
        check(st == 200 and body.get("images"), "extra-batch float/string/SPZ rslt dims")
        st, body = http("GET", base, "/sdapi/v1/progress")
        check(st == 200 and "progress" in body, "progress")
        check("eta_relative" in body and "state" in body, "progress eta/state fields")
        st, body = http("POST", base, "/sdapi/v1/interrupt", {})
        check(st == 200, "interrupt")
        st, body = http("POST", base, "/sdapi/v1/unload-checkpoint", {})
        check(st == 200, "unload-checkpoint")

        st, body = http("POST", base, "/sdapi/v1/txt2img", {"width": 16, "height": 16, "prompt": "png"})
        check(st == 200 and body.get("images"), "txt2img png present")
        import base64
        raw_png = base64.b64decode(body["images"][0])
        check(raw_png.startswith(b"\x89PNG"), "txt2img payload is PNG bytes")

        # OPTIONS preflight
        req = urllib.request.Request(f"{base}/internal/ping", method="OPTIONS")
        with urllib.request.urlopen(req, timeout=5) as resp:
            check(int(resp.status) in (200, 204), "OPTIONS preflight")

        # Backend selection / guards
        try:
            be.RemoteForgeBackend("127.0.0.1:7860")
            check(False, "reject loopback remote")
        except be.BackendError:
            check(True, "reject loopback remote")
        try:
            be.RemoteForgeBackend("")
            check(False, "reject empty remote")
        except be.BackendError:
            check(True, "reject empty remote")
        try:
            be.FalBackend("x").generate("/sdapi/v1/txt2img", {})
            check(False, "fal not implemented")
        except be.BackendError as e:
            check(e.status == 501, "fal 501")

        b = be.build_backend("demo", "")
        check(b.name == "demo", "build demo")
        b = be.build_backend("colab", "https://example.trycloudflare.com")
        check(b.name == "remote_forge", "build colab as remote_forge")
        b = be.build_backend("colab", "example.trycloudflare.com")
        check(b.base_url.startswith("https://"), "hostname paste defaults to https")
        b = be.build_backend("colab", "10.9.8.7:8188")
        check(b.base_url.startswith("http://"), "LAN IP:port paste stays http")
        coerced = be._coerce_forge_images({"image": "QUJD", "html_info": ""})
        check(coerced.get("images") == ["QUJD"], "extras image coerced to images[]")
        b = be.build_backend("colab", '"https://example.trycloudflare.com/"')
        check(b.base_url == "https://example.trycloudflare.com", "strip quoted pasted URL")
        b = be.RemoteForgeBackend("https://example.trycloudflare.com/sdapi/v1")
        check(b.base_url == "https://example.trycloudflare.com", "strip pasted /sdapi/v1 suffix")
        b = be.RemoteForgeBackend("https://example.trycloudflare.com/controlnet/")
        check(b.base_url == "https://example.trycloudflare.com", "strip pasted /controlnet suffix")
        dead = be.RemoteForgeBackend("http://127.0.0.1:9")
        ok_p, msg_p = dead.probe(timeout_s=0.4)
        check(not ok_p, f"probe dead remote fails ({msg_p[:80]})")
        try:
            be.build_backend("fal", "key")
            check(False, "build fal fails at connect")
        except be.BackendError as e:
            check(e.status == 501, "build fal fails at connect")

        # Reconnect / already listening
        ok2, msg2 = shim.start_shim(host, port)
        check(ok2 and "already listening" in msg2, f"second start: {msg2}")
        check(shim.is_running(), "is_running")
        check(shim.listen_endpoint() == f"{host}:{port}", "listen_endpoint")
        ping_ok, ping_raw = shim.ping_listen()
        check(ping_ok and "cloud_inference" in ping_raw, "ping_listen uses listen_endpoint (not hardcoded :7860)")

        st, body = http("GET", base, "/does-not-exist")
        check(st == 404, "unknown GET 404")

        # cloud_inference marker
        st, body = http("GET", base, "/internal/ping")
        check(isinstance(body, dict) and body.get("cloud_inference") is True, "ping cloud_inference marker")

        # float dims
        st, body = http("POST", base, "/sdapi/v1/txt2img", {"width": 32.0, "height": 32.0, "prompt": "f"})
        check(st == 200 and body.get("images"), "txt2img float width/height")

    finally:
        ok_stop, stop_msg = shim.stop_shim()
        check(ok_stop, f"stop_shim: {stop_msg}")
        check(not shim.is_running(), "not running after stop")

    # Remote catalog proxy (separate ports) — must not return demo model names.
    try:
        from http.server import ThreadingHTTPServer

        up_port, shim_port = 17911, 17912

        class _Up(BaseHTTPRequestHandler):
            def log_message(self, *a):
                return

            def do_GET(self):
                if "sd-models" in self.path:
                    body = json.dumps(
                        [
                            {
                                "title": "upstream-real",
                                "model_name": "upstream-real",
                                "hash": "u",
                                "sha256": "",
                                "filename": "u.safetensors",
                            }
                        ]
                    ).encode()
                else:
                    body = b"{}"
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

        up = ThreadingHTTPServer(("127.0.0.1", up_port), _Up)
        up.daemon_threads = True
        threading.Thread(target=up.serve_forever, daemon=True).start()
        shim.get_state().set_backend(be.RemoteForgeBackend(f"http://127.0.0.1:{up_port}"))
        ok, msg = shim.start_shim("127.0.0.1", shim_port)
        check(ok, f"remote shim start: {msg}")
        time.sleep(0.1)
        st, body = http("GET", f"http://127.0.0.1:{shim_port}", "/sdapi/v1/sd-models")
        check(
            st == 200 and isinstance(body, list) and body and body[0].get("model_name") == "upstream-real",
            "remote sd-models proxies upstream",
        )
        check(
            not (isinstance(body, list) and body and body[0].get("model_name") == "cloud-inference-demo"),
            "remote sd-models is not demo stub",
        )
        live = shim.get_state().backend
        ok_p, msg_p = live.probe(timeout_s=2.0)
        check(ok_p, f"probe live remote ping ({msg_p[:60]})")
        shim.stop_shim()
        up.shutdown()
    except Exception as exc:
        check(False, f"remote catalog wiring: {exc}")

    # Interrupt must abort in-flight remote generate (Unity Abort + /interrupt).
    try:
        from http.server import ThreadingHTTPServer

        up_port, shim_port = 17921, 17922

        class _Slow(BaseHTTPRequestHandler):
            hits = {"txt2img": 0, "interrupt": 0}

            def log_message(self, *a):
                return

            def _ok(self, body: bytes):
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self):
                self._ok(b"{}")

            def do_POST(self):
                length = int(self.headers.get("Content-Length") or 0)
                if length:
                    self.rfile.read(length)
                if "interrupt" in self.path:
                    _Slow.hits["interrupt"] += 1
                    self._ok(b'{"interrupted": true}')
                    return
                _Slow.hits["txt2img"] += 1
                time.sleep(8.0)
                self._ok(b'{"images":["d2FzdGVk"]}')

        up = ThreadingHTTPServer(("127.0.0.1", up_port), _Slow)
        up.daemon_threads = True
        threading.Thread(target=up.serve_forever, daemon=True).start()
        shim.get_state().set_backend(be.RemoteForgeBackend(f"http://127.0.0.1:{up_port}"))
        ok, msg = shim.start_shim("127.0.0.1", shim_port)
        check(ok, f"interrupt-abort shim start: {msg}")
        time.sleep(0.1)
        base = f"http://127.0.0.1:{shim_port}"
        box = {"st": None, "body": None, "err": None}

        def _gen():
            try:
                box["st"], box["body"] = http("POST", base, "/sdapi/v1/txt2img", {"width": 16, "height": 16}, timeout=12)
            except Exception as exc:
                box["err"] = exc

        t = threading.Thread(target=_gen, daemon=True)
        t0 = time.time()
        t.start()
        time.sleep(0.35)
        st_i, body_i = http("POST", base, "/sdapi/v1/interrupt", {})
        check(st_i == 200 and body_i.get("interrupted") is True, "interrupt during remote generate")
        t.join(timeout=4.0)
        elapsed = time.time() - t0
        check(not t.is_alive(), "interrupt unblocks generate handler (did not wait full upstream 8s)")
        check(elapsed < 4.0, f"interrupt abort elapsed {elapsed:.2f}s < 4s")
        check(box["err"] is None, "interrupt generate thread no exception")
        if box["body"] is None:
            check(True, "interrupt generate response dropped (Unity-style abort)")
        else:
            check(
                box["body"].get("interrupted") is True or not box["body"].get("images"),
                "interrupt generate does not return a full image as success",
            )
        box2 = {"st": None, "body": None, "err": None}

        def _extra():
            try:
                box2["st"], box2["body"] = http(
                    "POST", base, "/sdapi/v1/extra-batch-images", {"upscaling_resize": 2}, timeout=12
                )
            except Exception as exc:
                box2["err"] = exc

        t2 = threading.Thread(target=_extra, daemon=True)
        t2.start()
        time.sleep(0.35)
        http("POST", base, "/sdapi/v1/interrupt", {})
        t2.join(timeout=4.0)
        check(not t2.is_alive(), "interrupt unblocks extra-batch handler")
        if box2["body"] is not None:
            check(
                box2["body"].get("interrupted") is True or not box2["body"].get("images"),
                "interrupt extra-batch does not return a full image as success",
            )
        else:
            check(box2["err"] is None, "interrupt extra-batch no exception")

        def _gen_for_stop():
            try:
                http("POST", base, "/sdapi/v1/txt2img", {"width": 16, "height": 16}, timeout=12)
            except Exception:
                pass

        t_stop = threading.Thread(target=_gen_for_stop, daemon=True)
        t_stop.start()
        time.sleep(0.35)
        t0_stop = time.time()
        ok_stop, stop_msg = shim.stop_shim()
        elapsed_stop = time.time() - t0_stop
        check(ok_stop, f"stop_shim during generate: {stop_msg}")
        check(elapsed_stop < 4.0, f"Disconnect during generate does not wait 300s ({elapsed_stop:.2f}s)")
        t_stop.join(timeout=4.0)
        up.shutdown()
    except Exception as exc:
        check(False, f"interrupt abort wiring: {exc}")

    print(f"\nDeep checks: {CHECKS}  fails: {FAILS}")
    return 0 if FAILS == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
