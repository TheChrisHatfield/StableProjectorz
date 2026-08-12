"""Backend adapters for CloudInferenceSPZ Forge shim.

Modes:
  demo         — local solid PNG so SPZ can validate connect + generate without GPU
  remote_forge — proxy HTTP to a Colab/RunPod/tunnel Forge base URL
  fal          — reserved (returns clear error until thick shim lands)
"""

from __future__ import annotations

import base64
import json
import struct
import urllib.error
import urllib.request
import zlib
from typing import Any, Dict, Optional, Tuple


# 64x64 dark slate PNG (valid Forge-style images[0] base64 payload).
def _png_chunk(tag: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)


def _make_solid_png_b64(width: int = 64, height: int = 64, rgb: Tuple[int, int, int] = (40, 44, 52)) -> str:
    width = max(8, min(2048, int(width or 64)))
    height = max(8, min(2048, int(height or 64)))
    r, g, b = rgb
    raw = b"".join(b"\x00" + bytes((r, g, b)) * width for _ in range(height))
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + _png_chunk(b"IHDR", ihdr) + _png_chunk(b"IDAT", zlib.compress(raw, 9)) + _png_chunk(b"IEND", b"")
    return base64.b64encode(png).decode("ascii")


class BackendError(RuntimeError):
    def __init__(self, message: str, status: int = 502):
        super().__init__(message)
        self.status = int(status)


class CloudBackend:
    """Interface used by forge_shim."""

    name = "base"

    def describe(self) -> str:
        return self.name

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        raise NotImplementedError

    def proxy(self, method: str, path: str, body: Optional[bytes], headers: Dict[str, str]) -> Tuple[int, bytes, str]:
        """Optional raw proxy. Return (status, body, content_type)."""
        raise BackendError(f"{self.name} does not proxy {method} {path}", status=501)


class DemoBackend(CloudBackend):
    name = "demo"

    def describe(self) -> str:
        return "demo (local solid PNG)"

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        try:
            w = int(float(payload.get("width") or 64))
        except (TypeError, ValueError):
            w = 64
        try:
            h = int(float(payload.get("height") or 64))
        except (TypeError, ValueError):
            h = 64
        # img2img often carries init size; prefer payload width/height.
        img_b64 = _make_solid_png_b64(w, h, rgb=(42, 96, 140) if "img2img" in path else (40, 44, 52))
        return {
            "images": [img_b64],
            "parameters": {},
            "info": json.dumps(
                {
                    "cloud_inference": "demo",
                    "path": path,
                    "width": w,
                    "height": h,
                    "seed": payload.get("seed", -1),
                }
            ),
        }


class RemoteForgeBackend(CloudBackend):
    """Proxy to a remote Forge/A1111 base (http://host:port or https tunnel)."""

    name = "remote_forge"

    def __init__(self, base_url: str, timeout_s: float = 300.0):
        base = (base_url or "").strip().rstrip("/")
        if not base:
            raise BackendError("Remote Forge URL / session code is empty", status=400)
        if "://" not in base:
            # Allow host:port paste without scheme.
            base = "http://" + base
        # Refuse loopback :7860 — that is the local shim itself (proxy would recurse).
        lowered = base.lower()
        if "127.0.0.1:7860" in lowered or "localhost:7860" in lowered or "[::1]:7860" in lowered:
            raise BackendError(
                "Remote URL cannot be 127.0.0.1:7860 (that is the local Cloud Inference shim). "
                "Paste a Colab/RunPod public Forge URL instead, or use Demo.",
                status=400,
            )
        self.base_url = base
        self.timeout_s = float(timeout_s)

    def describe(self) -> str:
        return f"remote_forge → {self.base_url}"

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        status, body, _ct = self.proxy("POST", path, json.dumps(payload).encode("utf-8"), {"Content-Type": "application/json"})
        if status >= 400:
            raise BackendError(body.decode("utf-8", errors="replace")[:500] or f"upstream {status}", status=status)
        try:
            data = json.loads(body.decode("utf-8"))
        except Exception as exc:
            raise BackendError(f"upstream returned non-JSON: {exc}", status=502) from exc
        if not isinstance(data, dict):
            raise BackendError("upstream JSON was not an object", status=502)
        return data

    def proxy(self, method: str, path: str, body: Optional[bytes], headers: Dict[str, str]) -> Tuple[int, bytes, str]:
        if not path.startswith("/"):
            path = "/" + path
        url = self.base_url + path
        req_headers = {
            k: v
            for k, v in headers.items()
            if k.lower()
            not in (
                "host",
                "content-length",
                "transfer-encoding",
                "connection",
                "keep-alive",
                "proxy-connection",
                "te",
                "trailers",
                "upgrade",
                "expect",
            )
        }
        req = urllib.request.Request(url, data=body, headers=req_headers, method=method.upper())
        try:
            with urllib.request.urlopen(req, timeout=self.timeout_s) as resp:
                raw = resp.read()
                ct = resp.headers.get("Content-Type") or "application/json"
                return int(resp.status), raw, ct
        except urllib.error.HTTPError as exc:
            raw = exc.read() if hasattr(exc, "read") else b""
            return int(exc.code), raw or str(exc).encode("utf-8"), "application/json"
        except Exception as exc:
            raise BackendError(f"proxy failed: {exc}", status=502) from exc


class FalBackend(CloudBackend):
    """Placeholder — thick fal→Forge translation is P2."""

    name = "fal"

    def __init__(self, api_key: str = ""):
        self.api_key = (api_key or "").strip()

    def describe(self) -> str:
        return "fal (not implemented — use Demo or Remote Forge/Colab)"

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        raise BackendError(
            "fal backend is not wired yet. Use Demo to validate SPZ, or paste a Colab/RunPod Forge URL.",
            status=501,
        )


def build_backend(mode: str, credential: str) -> CloudBackend:
    mode_n = (mode or "demo").strip().lower()
    cred = (credential or "").strip()
    if mode_n in ("demo", "local_demo"):
        return DemoBackend()
    if mode_n in ("remote_forge", "remote", "colab", "runpod", "tunnel"):
        return RemoteForgeBackend(cred)
    if mode_n in ("fal", "fal.ai"):
        return FalBackend(cred)
    raise BackendError(f"Unknown backend mode: {mode}", status=400)
