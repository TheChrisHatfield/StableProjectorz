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
    init_path = os.path.join(_root, "__init__.py")
    with open(init_path, encoding="utf-8") as f:
        init_src = f.read()
    check("add_toggle(" in init_src, "panel uses toggle for auto-connect")
    check("add_foldout(" in init_src, "panel folds rare actions under More")
    check("API key / Remote URL" in init_src, "credential field accepts fal key or Forge URL")
    check("Auto-connect (On/Off)" not in init_src, "auto-connect is not a free-text On/Off field")
    check("fal — paste API key" in init_src, "fal backend is offered as API-key mode")
    # Connect honesty: do not set_backend before the local shim is proven up.
    connect_fn = init_src.find("def connect_cloud")
    check(connect_fn > 0, "connect_cloud present")
    if connect_fn > 0:
        body = init_src[connect_fn: init_src.find("\ndef disconnect_cloud", connect_fn)]
        i_start = body.find("shim.start_shim(")
        i_ping = body.find("_ping_local_shim(")
        i_set = body.find("set_backend(")
        check(
            i_start >= 0 and i_ping >= 0 and i_set >= 0 and i_start < i_ping < i_set,
            "Connect sets backend only after start_shim + ping",
        )
    disc_fn = init_src.find("def disconnect_cloud")
    check(disc_fn > 0, "disconnect_cloud present")
    if disc_fn > 0:
        disc_body = init_src[disc_fn : init_src.find("\ndef ", disc_fn + 1)]
        check(
            "mark_sd_disconnected" in disc_body and "stop_shim" in disc_body,
            "Disconnect stops shim and marks SERV disconnected",
        )

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
            be.FalBackend("").generate("/sdapi/v1/txt2img", {})
            check(False, "fal empty key rejected")
        except be.BackendError as e:
            check(e.status == 400, "fal empty key rejected")

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
        fal_b = be.build_backend("fal", "test-key-not-real")
        check(fal_b.name == "fal", "build fal with key succeeds")
        check("Key " in fal_b._auth_headers().get("Authorization", ""), "fal auth header uses Key prefix")
        try:
            be.build_backend("fal", "")
            check(False, "build fal empty key fails")
        except be.BackendError as e:
            check(e.status == 400, "build fal empty key fails")
        size = be._pick_fal_image_size(1024, 768)
        check(size == "landscape_4_3", "fal size maps 1024x768 to landscape_4_3")
        size_near = be._pick_fal_image_size(1000, 760)
        check(
            isinstance(size_near, dict) and size_near.get("width") == 1000 and size_near.get("height") == 760,
            "fal size does not silently snap near-preset dims",
        )
        size_custom = be._pick_fal_image_size(640, 480)
        check(isinstance(size_custom, dict) and size_custom.get("width") == 640, "fal odd size uses custom width/height")
        fal_map = be.FalBackend("k", queue_base="http://127.0.0.1:9")
        img2 = fal_map._forge_payload_to_fal({"prompt": "x", "steps": 4, "init_images": ["QUJD"], "width": 512, "height": 512}, img2img=True)
        check(img2.get("num_inference_steps") == 10, "fal img2img clamps steps to schema min 10")
        check("image_size" not in img2, "fal img2img omits txt2img-only image_size")
        check(img2.get("image_url", "").startswith("data:image/png;base64,"), "fal img2img wraps init as data URI")
        batch = fal_map._forge_payload_to_fal({"prompt": "x", "batch_size": 3, "width": 512, "height": 512}, img2img=False)
        check(batch.get("num_images") == 3, "fal txt2img maps Forge batch_size to num_images")
        batch_hi = fal_map._forge_payload_to_fal({"prompt": "x", "batch_size": 99, "width": 512, "height": 512}, img2img=False)
        check(batch_hi.get("num_images") == 4, "fal txt2img clamps batch_size to fal max 4")
        n_iter = fal_map._forge_payload_to_fal(
            {"prompt": "x", "batch_size": 1, "n_iter": 3, "width": 512, "height": 512}, img2img=False
        )
        check(n_iter.get("num_images") == 3, "fal txt2img honors n_iter when batch_size is 1")
        check(batch.get("sync_mode") is True, "fal requests sync_mode data-URI images")
        try:
            fal_map.generate(
                "/sdapi/v1/img2img",
                {
                    "prompt": "x",
                    "init_images": ["QUJD"],
                    "mask": be._make_half_mask_png_b64(16, 16),
                    "width": 32,
                    "height": 32,
                },
            )
            check(False, "fal img2img with selective mask must 501")
        except be.BackendError as e:
            check(
                e.status == 501 and "mask" in str(e).lower(),
                "fal img2img with selective mask honestly returns 501",
            )
        white_mask = be._make_solid_png_b64(16, 16, rgb=(255, 255, 255))
        try:
            fal_map.generate(
                "/sdapi/v1/img2img",
                {
                    "prompt": "x",
                    "init_images": [white_mask],
                    "mask": white_mask,
                    "width": 16,
                    "height": 16,
                },
            )
            check(False, "fal img2img full-white mask should pass gate (then fail upstream)")
        except be.BackendError as e:
            check(
                e.status != 501,
                f"fal img2img full-white mask must not 501 (got {e.status}: {e})",
            )
        try:
            fal_map.generate(
                "/sdapi/v1/txt2img",
                {
                    "prompt": "x",
                    "width": 32,
                    "height": 32,
                    "alwayson_scripts": {
                        "controlnet": {
                            "args": [{"enabled": True, "model": "control_v11f1p_sd15_depth", "module": "None", "image": ""}]
                        }
                    },
                },
            )
            check(False, "fal txt2img with active ControlNet must 501")
        except be.BackendError as e:
            check(
                e.status == 501 and "controlnet" in str(e).lower(),
                "fal txt2img with ControlNet honestly returns 501",
            )
        try:
            fal_map.generate(
                "/sdapi/v1/img2img",
                {
                    "prompt": "x",
                    "init_images": [be._make_solid_png_b64(8, 8)],
                    "mask": be._make_solid_png_b64(8, 8, rgb=(255, 255, 255)),
                    "width": 8,
                    "height": 8,
                    "alwayson_scripts": {"Soft Inpainting": {"args": [True, 0.5]}},
                },
            )
            check(False, "fal Soft Inpainting must 501")
        except be.BackendError as e:
            check(
                e.status == 501 and "soft" in str(e).lower(),
                "fal Soft Inpainting honestly returns 501",
            )
        png_b64 = be._make_solid_png_b64(16, 16)
        forged = be._fal_result_images_to_b64({"images": [{"url": f"data:image/png;base64,{png_b64}"}]})
        check(forged == [png_b64], "fal data-URI images coerce to Forge b64")

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
                (box.get("st") or 0) >= 400
                or box["body"].get("interrupted") is True
                or not box["body"].get("images"),
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
                box2["st"] >= 400 or box2["body"].get("interrupted") is True or not box2["body"].get("images"),
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

    # fal thick shim against a local mock queue (no real fal key / network).
    try:
        from http.server import ThreadingHTTPServer

        fal_port, shim_fal_port = 17931, 17932
        png_b64 = be._make_solid_png_b64(16, 16)
        state = {"cancelled": False, "auth_ok": False, "probe_queued": False}

        class _FalQueue(BaseHTTPRequestHandler):
            def log_message(self, *a):
                return

            def _read(self):
                n = int(self.headers.get("Content-Length") or 0)
                return self.rfile.read(n) if n else b""

            def _json(self, code, obj):
                raw = json.dumps(obj).encode("utf-8")
                self.send_response(code)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(raw)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(raw)

            def do_POST(self):
                auth = self.headers.get("Authorization") or ""
                if auth != "Key good-key":
                    self._json(401, {"detail": "Unauthorized"})
                    return
                if state.get("force_404"):
                    self._json(404, {"detail": "model not found"})
                    return
                delay = float(state.get("delay_post_s") or 0)
                if delay > 0:
                    time.sleep(delay)
                state["auth_ok"] = True
                body = self._read()
                try:
                    payload = json.loads(body.decode("utf-8")) if body else {}
                except Exception:
                    payload = {}
                prompt = str(payload.get("prompt") or "")
                rid = "req-mock-1"
                base_q = f"http://127.0.0.1:{fal_port}/fal-ai/flux/schnell"
                # Empty prompt: simulate a gateway that still queues (must be cancelled by probe).
                if not prompt.strip():
                    state["probe_queued"] = True
                    self._json(
                        200,
                        {
                            "request_id": rid + "-probe",
                            "cancel_url": f"{base_q}/requests/{rid}-probe/cancel",
                        },
                    )
                    return
                # Omit response_url on purpose so FalBackend must reconstruct …/response.
                self._json(
                    200,
                    {
                        "request_id": rid,
                        "status_url": f"{base_q}/requests/{rid}/status",
                        "cancel_url": f"{base_q}/requests/{rid}/cancel",
                    },
                )

            def do_GET(self):
                auth = self.headers.get("Authorization") or ""
                if auth != "Key good-key":
                    self._json(401, {"detail": "Unauthorized"})
                    return
                if self.path.endswith("/status") or "/status?" in self.path:
                    if "does-not-exist" in self.path:
                        self._json(404, {"detail": "not found"})
                        return
                    self._json(200, {"status": "COMPLETED"})
                    return
                # OpenAPI result path is bare …/requests/{id}; also accept …/response.
                if "/requests/" in self.path and not self.path.rstrip("/").endswith("/cancel"):
                    if "does-not-exist" in self.path:
                        self._json(404, {"detail": "not found"})
                        return
                    self._json(
                        200,
                        {"images": [{"url": f"data:image/png;base64,{png_b64}"}], "seed": 1},
                    )
                    return
                self._json(404, {"detail": "missing"})

            def do_PUT(self):
                if self.path.endswith("/cancel"):
                    state["cancelled"] = True
                    self._json(200, {"status": "CANCELLED"})
                    return
                self._json(404, {"detail": "missing"})

        fal_srv = ThreadingHTTPServer(("127.0.0.1", fal_port), _FalQueue)
        fal_thr = threading.Thread(target=fal_srv.serve_forever, daemon=True)
        fal_thr.start()
        time.sleep(0.05)

        bad = be.FalBackend("bad-key", queue_base=f"http://127.0.0.1:{fal_port}")
        ok_bad, msg_bad = bad.probe(timeout_s=2.0)
        check(not ok_bad, f"fal probe rejects bad key ({msg_bad[:60]})")

        good = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}")
        ok_good, msg_good = good.probe(timeout_s=2.0)
        check(ok_good, f"fal probe accepts good key ({msg_good[:60]})")
        check(state["probe_queued"] and state["cancelled"], "fal probe cancels accidental queue on Connect")
        state["cancelled"] = False
        state["force_404"] = True
        ok_404, msg_404 = good.probe(timeout_s=2.0)
        check(not ok_404 and "404" in msg_404, f"fal probe fails closed on unexpected 404 ({msg_404[:80]})")
        state["force_404"] = False

        result = good.generate(
            "/sdapi/v1/txt2img",
            {"prompt": "a cat", "width": 16, "height": 16, "steps": 4},
        )
        check(
            isinstance(result.get("images"), list) and result["images"] and result["images"][0] == png_b64,
            "fal queue txt2img returns Forge images[]",
        )
        result_neg = good.generate(
            "/sdapi/v1/txt2img",
            {
                "prompt": "a cat",
                "negative_prompt": "blurry, low quality",
                "width": 16,
                "height": 16,
                "steps": 4,
            },
        )
        try:
            info_neg = json.loads(result_neg.get("info") or "{}")
        except Exception:
            info_neg = {}
        check(
            info_neg.get("negative_prompt_ignored") is True,
            "fal info marks negative_prompt as ignored (FLUX has no negatives)",
        )

        # Poll must fail fast on missing status (not spin until timeout).
        missing = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}", timeout_s=3.0)
        try:
            # Force a status URL that 404s by patching after a normal submit shape.
            missing.begin_job()
            t0 = time.time()
            st_code, st_body, _ = missing._http_json(
                "GET",
                f"http://127.0.0.1:{fal_port}/fal-ai/flux/schnell/requests/does-not-exist/status",
                None,
                timeout_s=2.0,
            )
            check(st_code == 404, "fal missing status returns HTTP 404")
            check(time.time() - t0 < 2.5, "fal status 404 does not hang")
        except be.BackendError as exc:
            check("404" in str(exc) or "not found" in str(exc).lower(), f"fal status 404 surfaces ({exc})")

        # Interrupt cancels the live fal request URL.
        slow = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}", timeout_s=8.0)
        box_f = {"exc": None}

        def _slow_gen():
            try:
                # After submit, poll status forever if we never complete — force cancel mid-flight.
                slow.begin_job()
                # Manually set cancel URL then abort like interrupt does.
                submit = slow._http_json(
                    "POST",
                    f"http://127.0.0.1:{fal_port}/fal-ai/flux/schnell",
                    {"prompt": "x"},
                    timeout_s=2.0,
                )
                meta = json.loads(submit[1].decode("utf-8"))
                with slow._io_lock:
                    slow._cancel_url = meta.get("cancel_url")
                slow.abort()
            except Exception as exc:
                box_f["exc"] = exc

        _slow_gen()
        check(state["cancelled"], "fal abort hits cancel_url")

        # Abort mid-submit (no cancel_url yet) must still PUT cancel after request_id arrives.
        state["delay_post_s"] = 0.2
        state["cancelled"] = False
        race = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}", timeout_s=8.0)
        box_race = {"exc": None}

        def _race_gen():
            try:
                race.begin_job()
                race.generate(
                    "/sdapi/v1/txt2img",
                    {"prompt": "race", "width": 16, "height": 16, "steps": 4},
                )
            except Exception as exc:
                box_race["exc"] = exc

        thr_race = threading.Thread(target=_race_gen, daemon=True)
        thr_race.start()
        time.sleep(0.05)
        race.abort()
        thr_race.join(timeout=5.0)
        state["delay_post_s"] = 0
        check(
            isinstance(box_race.get("exc"), be.BackendError) and box_race["exc"].status == 499,
            f"abort mid-submit raises interrupted ({box_race.get('exc')})",
        )
        check(state["cancelled"], "abort mid-submit still cancels fal after request_id")

        # set_backend mid-job must abort the previous fal instance (not orphan cancel_url).
        owner = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}", timeout_s=8.0)
        owner.begin_job()
        owner._cancel_url = f"http://127.0.0.1:{fal_port}/fal-ai/flux/schnell/requests/req-mid/cancel"
        shim.get_state().set_backend(owner)
        state["cancelled"] = False
        replacement = be.FalBackend("good-key", queue_base=f"http://127.0.0.1:{fal_port}")
        shim.get_state().set_backend(replacement)
        check(state["cancelled"], "set_backend mid-job aborts previous fal cancel_url")

        # End-to-end through the local Forge shim with fal backend.
        if shim.is_port_free("127.0.0.1", shim_fal_port):
            shim.get_state().set_backend(good)
            ok_fs, msg_fs = shim.start_shim("127.0.0.1", shim_fal_port)
            check(ok_fs, f"fal shim start: {msg_fs}")
            st, body = http("GET", f"http://127.0.0.1:{shim_fal_port}", "/sdapi/v1/sd-models")
            check(
                st == 200
                and isinstance(body, list)
                and body
                and body[0].get("model_name") == "fal-flux-schnell"
                and "img2img=dev" in str(body[0].get("title") or ""),
                "fal shim catalogs disclose schnell txt2img + flux/dev img2img",
            )
            st, body = http(
                "POST",
                f"http://127.0.0.1:{shim_fal_port}",
                "/sdapi/v1/txt2img",
                {"prompt": "cloud", "width": 16, "height": 16},
                timeout=20,
            )
            check(st == 200 and body.get("images") and body["images"][0] == png_b64, "fal shim txt2img")
            st, body = http(
                "POST",
                f"http://127.0.0.1:{shim_fal_port}",
                "/sdapi/v1/extra-batch-images",
                {"upscaling_resize": 2},
                timeout=10,
            )
            check(st == 501, "fal shim extras honestly returns 501 (no fake Demo PNG)")
            st, body = http(
                "POST",
                f"http://127.0.0.1:{shim_fal_port}",
                "/controlnet/detect",
                {"controlnet_module": "depth", "controlnet_input_images": ["QUJD"]},
                timeout=10,
            )
            check(st == 501, "fal shim detect honestly returns 501 (no echo fake preprocess)")
            st, body = http(
                "POST",
                f"http://127.0.0.1:{shim_fal_port}",
                "/sdapi/v1/options",
                {"sd_model_checkpoint": "some-other-ckpt", "sd_vae": "fake-vae"},
                timeout=10,
            )
            check(
                st == 200
                and isinstance(body, dict)
                and body.get("sd_model_checkpoint") == "fal-flux-schnell"
                and body.get("sd_vae") == "Automatic",
                "fal shim options refuses fake checkpoint/VAE swap",
            )
            shim.stop_shim()
        else:
            check(False, "fal shim port busy")

        fal_srv.shutdown()
    except Exception as exc:
        check(False, f"fal thick shim wiring: {exc}")

    print(f"\nDeep checks: {CHECKS}  fails: {FAILS}")
    return 0 if FAILS == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
