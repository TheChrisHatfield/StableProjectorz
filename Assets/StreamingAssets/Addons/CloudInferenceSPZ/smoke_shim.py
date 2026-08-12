"""Smoke test for CloudInferenceSPZ Forge shim (no Unity required).

Run:
  py -3.11 Assets/StreamingAssets/Addons/CloudInferenceSPZ/smoke_shim.py
"""

from __future__ import annotations

import json
import os
import sys
import time
import urllib.request

_root = os.path.dirname(os.path.abspath(__file__))
if _root not in sys.path:
    sys.path.insert(0, _root)

import backends as be
import forge_shim as shim


def main() -> int:
    # Use an alternate port if 7860 is busy so CI/dev machines with Forge running still validate.
    host, port = "127.0.0.1", 7860
    if not shim.is_port_free(host, port):
        port = 17860
        print(f"[smoke] :7860 busy — using {port} for this smoke only")

    shim.get_state().set_backend(be.DemoBackend())
    ok, msg = shim.start_shim(host, port)
    if not ok:
        print("FAIL start:", msg)
        return 1
    print("OK start:", msg)
    time.sleep(0.2)

    base = f"http://{host}:{port}"

    def get(path: str):
        with urllib.request.urlopen(f"{base}{path}", timeout=5) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))

    def post(path: str, payload: dict):
        data = json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            f"{base}{path}",
            data=data,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, json.loads(resp.read().decode("utf-8"))

    try:
        st, body = get("/internal/ping")
        assert st == 200 and body.get("status") == "ok", body
        print("OK ping")

        st, body = get("/internal/sysinfo")
        assert st == 200, body
        assert body.get("Data path"), body
        assert str(body.get("Version", "")).lower().startswith("neo"), body
        assert "forge" in str(body.get("Data path", "")).lower(), body
        print("OK sysinfo", body.get("Data path"), body.get("Version"))

        st, body = get("/sdapi/v1/options")
        assert st == 200 and "sd_model_checkpoint" in body, body
        print("OK options")

        st, body = get("/sdapi/v1/sd-models")
        assert st == 200 and isinstance(body, list) and body and body[0].get("model_name"), body
        print("OK sd-models", body[0].get("model_name"))

        st, body = get("/sdapi/v1/samplers")
        assert st == 200 and isinstance(body, list) and body[0].get("name"), body
        print("OK samplers", body[0].get("name"))

        st, body = get("/sdapi/v1/sd-vae")
        assert st == 200 and isinstance(body, list), body
        print("OK sd-vae", len(body))

        st, body = post("/sdapi/v1/txt2img", {"width": 64, "height": 64, "prompt": "smoke"})
        assert st == 200 and body.get("images") and len(body["images"][0]) > 32, body
        print("OK txt2img images[0] len", len(body["images"][0]))

        st, body = post("/sdapi/v1/img2img", {"width": 64, "height": 64, "prompt": "smoke", "init_images": []})
        assert st == 200 and body.get("images") and len(body["images"][0]) > 32, body
        print("OK img2img images[0] len", len(body["images"][0]))

        st, body = get("/sdapi/v1/progress")
        assert st == 200 and "progress" in body, body
        assert float(body.get("progress") or 0) == 0.0, body
        print("OK progress idle", body.get("progress"))

        st, body = post("/sdapi/v1/interrupt", {})
        assert st == 200, body
        print("OK interrupt")
    finally:
        shim.stop_shim()

    print("PASS CloudInferenceSPZ shim smoke")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
