"""Backend adapters for CloudInferenceSPZ Forge shim.

Modes:
  demo         — local solid PNG so SPZ can validate connect + generate without GPU
  remote_forge — proxy HTTP to a Colab/RunPod/tunnel Forge base URL
  fal          — thick translator: API key → fal queue → Forge-shaped images[]
"""

from __future__ import annotations

import base64
import http.client
import json
import struct
import threading
import time
import urllib.error
import urllib.parse
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


def _coerce_forge_images(data: Dict[str, Any]) -> Dict[str, Any]:
    """SPZ Complete_PendingImages reads images[]. Some extras routes only set image."""
    if not isinstance(data, dict):
        return data
    images = data.get("images")
    if isinstance(images, list) and any(isinstance(x, str) and x for x in images):
        return data
    single = data.get("image")
    if isinstance(single, str) and single:
        out = dict(data)
        out["images"] = [single]
        return out
    return data


def _looks_like_lan_host(hostport: str) -> bool:
    """True for localhost / IPv4 / IPv6 literals (use http). Hostnames like trycloudflare.com need https."""
    host = (hostport or "").split("/")[0].strip().lower()
    if not host:
        return False
    if host.startswith("["):
        return True
    hostname = host.rsplit(":", 1)[0] if host.count(":") == 1 else host
    if hostname in ("localhost", "127.0.0.1", "0.0.0.0", "::1"):
        return True
    parts = hostname.split(".")
    if len(parts) == 4:
        try:
            return all(0 <= int(p) <= 255 for p in parts)
        except ValueError:
            return False
    return False


class CloudBackend:
    """Interface used by forge_shim."""

    name = "base"

    def describe(self) -> str:
        return self.name

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        raise NotImplementedError

    def abort(self) -> None:
        """Cancel an in-flight generate/proxy if the backend supports it."""
        return

    def probe(self, timeout_s: float = 8.0) -> Tuple[bool, str]:
        """Reachability check used at Connect. Demo is always reachable."""
        return True, self.name

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
            # LAN IP:port stays HTTP; Colab/trycloudflare/runpod hostnames are HTTPS.
            scheme = "http" if _looks_like_lan_host(base) else "https"
            base = scheme + "://" + base
        # Users often paste the API root from docs (…/sdapi/v1). Proxy already appends those paths.
        base = base.rstrip("/")
        for suffix in ("/sdapi/v1", "/sdapi", "/internal", "/controlnet"):
            if base.lower().endswith(suffix):
                base = base[: -len(suffix)].rstrip("/")
                break
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
        self._io_lock = threading.Lock()
        self._active_conn: Any = None
        self._gen_epoch = 0
        self._aborted = False

    def describe(self) -> str:
        return f"remote_forge → {self.base_url}"

    def probe(self, timeout_s: float = 8.0) -> Tuple[bool, str]:
        """GET /internal/ping on the pasted Forge so Connect cannot go green on a dead URL."""
        old = self.timeout_s
        self.timeout_s = float(timeout_s)
        try:
            status, body, _ct = self.proxy("GET", "/internal/ping", None, {})
            snippet = body.decode("utf-8", errors="replace")[:180] if body else ""
            if status != 200:
                return False, f"remote ping HTTP {status}: {snippet or 'empty body'}"
            try:
                data = json.loads(body.decode("utf-8")) if body else {}
            except Exception:
                return False, f"remote ping was not JSON (need a Forge/WebUI URL): {snippet}"
            if not isinstance(data, dict):
                return False, "remote ping JSON was not an object"
            return True, snippet or "ok"
        except BackendError as exc:
            return False, str(exc)
        finally:
            self.timeout_s = old

    def begin_job(self) -> None:
        with self._io_lock:
            self._aborted = False
            self._gen_epoch += 1
            conn = self._active_conn
            self._active_conn = None
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass

    def abort(self) -> None:
        """Close the live HTTP connection so a long generate does not outlive Unity Interrupt."""
        with self._io_lock:
            self._aborted = True
            self._gen_epoch += 1
            conn = self._active_conn
            self._active_conn = None
        if conn is None:
            return
        try:
            conn.close()
        except Exception:
            pass
        sock = getattr(conn, "sock", None)
        if sock is not None:
            try:
                import socket as _socket

                sock.shutdown(_socket.SHUT_RDWR)
            except Exception:
                pass
            try:
                sock.close()
            except Exception:
                pass

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        with self._io_lock:
            if self._aborted:
                raise BackendError("interrupted", status=499)
        status, body, _ct = self.proxy("POST", path, json.dumps(payload).encode("utf-8"), {"Content-Type": "application/json"})
        if status >= 400:
            raise BackendError(body.decode("utf-8", errors="replace")[:500] or f"upstream {status}", status=status)
        try:
            data = json.loads(body.decode("utf-8"))
        except Exception as exc:
            raise BackendError(f"upstream returned non-JSON: {exc}", status=502) from exc
        if not isinstance(data, dict):
            raise BackendError("upstream JSON was not an object", status=502)
        return _coerce_forge_images(data)

    def proxy(self, method: str, path: str, body: Optional[bytes], headers: Dict[str, str]) -> Tuple[int, bytes, str]:
        if not path.startswith("/"):
            path = "/" + path
        url = self.base_url + path
        parsed = urllib.parse.urlparse(url)
        host = parsed.hostname or "127.0.0.1"
        port = parsed.port or (443 if parsed.scheme == "https" else 80)
        req_path = parsed.path or "/"
        if parsed.query:
            req_path = req_path + "?" + parsed.query
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
        req_headers.setdefault("Connection", "close")
        if body is not None:
            req_headers["Content-Length"] = str(len(body))

        with self._io_lock:
            gen_path = any(x in path for x in ("txt2img", "img2img", "extra-batch-images"))
            if self._aborted and gen_path:
                raise BackendError("interrupted", status=499)
            epoch = self._gen_epoch

        conn: Any = None
        try:
            if parsed.scheme == "https":
                conn = http.client.HTTPSConnection(host, port, timeout=self.timeout_s)
            else:
                conn = http.client.HTTPConnection(host, port, timeout=self.timeout_s)
            with self._io_lock:
                if self._gen_epoch != epoch:
                    raise BackendError("interrupted", status=499)
                self._active_conn = conn
            conn.request(method.upper(), req_path, body=body, headers=req_headers)
            resp = conn.getresponse()
            with self._io_lock:
                if self._gen_epoch != epoch:
                    raise BackendError("interrupted", status=499)
            raw = resp.read()
            with self._io_lock:
                if self._gen_epoch != epoch:
                    raise BackendError("interrupted", status=499)
            ct = resp.getheader("Content-Type") or "application/json"
            return int(resp.status), raw, ct
        except BackendError:
            raise
        except Exception as exc:
            with self._io_lock:
                aborted = bool(self._aborted) or self._gen_epoch != epoch
            if aborted:
                raise BackendError("interrupted", status=499) from exc
            raise BackendError(f"proxy failed: {exc}", status=502) from exc
        finally:
            with self._io_lock:
                if self._active_conn is conn:
                    self._active_conn = None
            if conn is not None:
                try:
                    conn.close()
                except Exception:
                    pass


def _pick_fal_image_size(width: int, height: int) -> Any:
    """Map Forge W×H to fal size presets, or custom width/height when far from presets."""
    w = max(8, min(2048, int(width or 512)))
    h = max(8, min(2048, int(height or 512)))
    presets = {
        "square_hd": (1024, 1024),
        "square": (512, 512),
        "portrait_4_3": (768, 1024),
        "portrait_16_9": (576, 1024),
        "landscape_4_3": (1024, 768),
        "landscape_16_9": (1024, 576),
    }
    best = None
    best_err = None
    for name, (pw, ph) in presets.items():
        err = abs(pw - w) + abs(ph - h)
        if best_err is None or err < best_err:
            best_err = err
            best = name
    # Within ~128px of a preset → use the named size; else pass exact dims fal accepts.
    if best is not None and best_err is not None and best_err <= 128:
        return best
    return {"width": w, "height": h}


def _forge_init_to_data_uri(payload: Dict[str, Any]) -> str:
    """Forge img2img init_images[0] is raw/base64; fal wants an image_url (data URI ok)."""
    images = payload.get("init_images") or payload.get("init_image") or []
    if isinstance(images, str):
        images = [images]
    if not isinstance(images, list) or not images:
        raise BackendError("img2img requires init_images[0]", status=400)
    raw = images[0]
    if not isinstance(raw, str) or not raw.strip():
        raise BackendError("img2img init_images[0] empty", status=400)
    raw = raw.strip()
    if raw.startswith("data:"):
        return raw
    # Strip optional data-url / whitespace noise from SPZ.
    if "," in raw and raw.lower().startswith("data:"):
        return raw
    return "data:image/png;base64," + raw


def _fal_result_images_to_b64(data: Dict[str, Any]) -> list:
    """fal returns images[{url|file_data|...}]; Forge wants images[base64]."""
    out = []
    images = data.get("images") if isinstance(data, dict) else None
    if not isinstance(images, list):
        images = []
    for item in images:
        if isinstance(item, str) and item:
            if item.startswith("data:") and "," in item:
                out.append(item.split(",", 1)[1])
            else:
                out.append(_download_url_b64(item))
            continue
        if not isinstance(item, dict):
            continue
        for key in ("file_data", "data", "b64_json", "content"):
            val = item.get(key)
            if isinstance(val, str) and val:
                if val.startswith("data:") and "," in val:
                    out.append(val.split(",", 1)[1])
                else:
                    out.append(val)
                break
        else:
            url = item.get("url")
            if isinstance(url, str) and url:
                if url.startswith("data:") and "," in url:
                    out.append(url.split(",", 1)[1])
                else:
                    out.append(_download_url_b64(url))
    return out


def _download_url_b64(url: str, timeout_s: float = 60.0) -> str:
    req = urllib.request.Request(url, method="GET")
    with urllib.request.urlopen(req, timeout=timeout_s) as resp:
        raw = resp.read()
    if not raw:
        raise BackendError("fal image URL returned empty body", status=502)
    return base64.b64encode(raw).decode("ascii")


class FalBackend(CloudBackend):
    """Thick fal→Forge translator: API key Connect, queue txt2img/img2img, cancel on interrupt.

    Catalogs stay local stubs (Forge dropdowns). ControlNet detect stays Demo echo.
    """

    name = "fal"
    TXT2IMG_MODEL = "fal-ai/flux/schnell"
    IMG2IMG_MODEL = "fal-ai/flux/dev/image-to-image"

    def __init__(self, api_key: str = "", queue_base: str = "https://queue.fal.run", timeout_s: float = 300.0):
        key = (api_key or "").strip().strip("\"'")
        if not key:
            raise BackendError(
                "fal API key is empty. Paste a key from https://fal.ai/dashboard/keys",
                status=400,
            )
        # Users sometimes paste "Key xxx" from docs.
        if key.lower().startswith("key "):
            key = key[4:].strip()
        self.api_key = key
        self.queue_base = (queue_base or "https://queue.fal.run").rstrip("/")
        self.timeout_s = float(timeout_s)
        self._io_lock = threading.Lock()
        self._aborted = False
        self._gen_epoch = 0
        self._cancel_url: Optional[str] = None
        self._active_conn: Any = None

    def describe(self) -> str:
        return f"fal → {self.TXT2IMG_MODEL}"

    def _auth_headers(self) -> Dict[str, str]:
        return {
            "Authorization": f"Key {self.api_key}",
            "Content-Type": "application/json",
            "Connection": "close",
        }

    def begin_job(self) -> None:
        with self._io_lock:
            self._aborted = False
            self._gen_epoch += 1
            self._cancel_url = None
            conn = self._active_conn
            self._active_conn = None
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass

    def abort(self) -> None:
        with self._io_lock:
            self._aborted = True
            self._gen_epoch += 1
            cancel_url = self._cancel_url
            self._cancel_url = None
            conn = self._active_conn
            self._active_conn = None
        if conn is not None:
            try:
                conn.close()
            except Exception:
                pass
        if not cancel_url:
            return
        try:
            req = urllib.request.Request(
                cancel_url,
                data=b"{}",
                headers=self._auth_headers(),
                method="PUT",
            )
            with urllib.request.urlopen(req, timeout=8) as resp:
                resp.read()
        except Exception:
            # Best-effort cancel; generate path already treats abort as interrupted.
            pass

    def probe(self, timeout_s: float = 8.0) -> Tuple[bool, str]:
        """Auth check without leaving a billed job: empty prompt, cancel if queued."""
        url = f"{self.queue_base}/{self.TXT2IMG_MODEL}"
        try:
            status, body, _ct = self._http_json("POST", url, {"prompt": ""}, timeout_s=float(timeout_s))
        except BackendError as exc:
            return False, str(exc)
        snippet = (body.decode("utf-8", errors="replace") if isinstance(body, (bytes, bytearray)) else str(body))[:180]
        if status in (401, 403):
            return False, f"fal key rejected (HTTP {status})"
        if status >= 500:
            return False, f"fal probe upstream error HTTP {status}: {snippet}"
        # If fal accepted and queued (HTTP 200 + request_id), cancel immediately so Connect is not a paid generate.
        try:
            meta = json.loads(body.decode("utf-8")) if body else {}
        except Exception:
            meta = {}
        if isinstance(meta, dict):
            cancel_url = meta.get("cancel_url") or meta.get("cancelUrl")
            request_id = meta.get("request_id") or meta.get("requestId")
            if not cancel_url and request_id:
                cancel_url = f"{self.queue_base}/{self.TXT2IMG_MODEL}/requests/{request_id}/cancel"
            if cancel_url:
                try:
                    req = urllib.request.Request(
                        str(cancel_url),
                        data=b"{}",
                        headers=self._auth_headers(),
                        method="PUT",
                    )
                    with urllib.request.urlopen(req, timeout=min(8.0, float(timeout_s))) as resp:
                        resp.read()
                except Exception:
                    pass
        # 200 (queued, now cancelled) / 422 (validation) both mean auth worked.
        return True, snippet or f"HTTP {status}"

    def generate(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        with self._io_lock:
            if self._aborted:
                raise BackendError("interrupted", status=499)
        is_img2img = "img2img" in (path or "")
        model = self.IMG2IMG_MODEL if is_img2img else self.TXT2IMG_MODEL
        fal_payload = self._forge_payload_to_fal(payload, img2img=is_img2img)
        submit_url = f"{self.queue_base}/{model}"
        status, body, _ct = self._http_json("POST", submit_url, fal_payload, timeout_s=min(60.0, self.timeout_s))
        if status in (401, 403):
            raise BackendError("fal API key rejected", status=status)
        if status >= 400:
            raise BackendError(
                body.decode("utf-8", errors="replace")[:500] if body else f"fal submit HTTP {status}",
                status=status,
            )
        try:
            meta = json.loads(body.decode("utf-8")) if body else {}
        except Exception as exc:
            raise BackendError(f"fal submit non-JSON: {exc}", status=502) from exc
        if not isinstance(meta, dict):
            raise BackendError("fal submit JSON was not an object", status=502)

        # Sync-style response already has images (rare on queue.fal.run, common on fal.run).
        if isinstance(meta.get("images"), list) and meta["images"]:
            return self._to_forge_result(meta, path, payload)

        request_id = meta.get("request_id") or meta.get("requestId")
        status_url = meta.get("status_url") or meta.get("statusUrl")
        response_url = meta.get("response_url") or meta.get("responseUrl")
        cancel_url = meta.get("cancel_url") or meta.get("cancelUrl")
        if not request_id and not status_url:
            raise BackendError(f"fal submit missing request_id: {str(meta)[:200]}", status=502)
        if not status_url:
            status_url = f"{self.queue_base}/{model}/requests/{request_id}/status"
        if not response_url:
            # fal queue contract: result is GET …/requests/{id}/response (not the bare request path).
            response_url = f"{self.queue_base}/{model}/requests/{request_id}/response"
        if not cancel_url:
            cancel_url = f"{self.queue_base}/{model}/requests/{request_id}/cancel"
        with self._io_lock:
            self._cancel_url = str(cancel_url)

        deadline = time.time() + self.timeout_s
        while time.time() < deadline:
            with self._io_lock:
                if self._aborted:
                    raise BackendError("interrupted", status=499)
            st_code, st_body, _ = self._http_json("GET", status_url + ("&" if "?" in status_url else "?") + "logs=0", None, timeout_s=30.0)
            try:
                st_obj = json.loads(st_body.decode("utf-8")) if st_body else {}
            except Exception:
                st_obj = {}
            status_name = ""
            if isinstance(st_obj, dict):
                status_name = str(st_obj.get("status") or st_obj.get("detail") or "")
            if status_name.upper() in ("COMPLETED", "OK", "SUCCESS"):
                break
            if status_name.upper() in ("FAILED", "ERROR", "CANCELLED", "CANCELED"):
                raise BackendError(f"fal job {status_name}: {str(st_obj)[:300]}", status=502)
            # Some gateways return the final payload on the status URL.
            if isinstance(st_obj, dict) and isinstance(st_obj.get("images"), list) and st_obj["images"]:
                return self._to_forge_result(st_obj, path, payload)
            time.sleep(0.45)
        else:
            raise BackendError("fal job timed out", status=504)

        with self._io_lock:
            if self._aborted:
                raise BackendError("interrupted", status=499)
        res_code, res_body, _ = self._http_json("GET", response_url, None, timeout_s=60.0)
        if res_code >= 400:
            raise BackendError(
                res_body.decode("utf-8", errors="replace")[:500] if res_body else f"fal result HTTP {res_code}",
                status=res_code,
            )
        try:
            result = json.loads(res_body.decode("utf-8")) if res_body else {}
        except Exception as exc:
            raise BackendError(f"fal result non-JSON: {exc}", status=502) from exc
        if not isinstance(result, dict):
            raise BackendError("fal result was not an object", status=502)
        # Nested payload shapes.
        if "images" not in result and isinstance(result.get("response"), dict):
            result = result["response"]
        if "images" not in result and isinstance(result.get("data"), dict):
            result = result["data"]
        return self._to_forge_result(result, path, payload)

    def _forge_payload_to_fal(self, payload: Dict[str, Any], img2img: bool) -> Dict[str, Any]:
        try:
            w = int(float(payload.get("width") or 512))
        except (TypeError, ValueError):
            w = 512
        try:
            h = int(float(payload.get("height") or 512))
        except (TypeError, ValueError):
            h = 512
        prompt = str(payload.get("prompt") or "").strip() or " "
        out: Dict[str, Any] = {
            "prompt": prompt,
            "num_images": 1,
            "enable_safety_checker": True,
            "output_format": "png",
            "image_size": _pick_fal_image_size(w, h),
        }
        try:
            steps = int(float(payload.get("steps") or (28 if img2img else 4)))
        except (TypeError, ValueError):
            steps = 28 if img2img else 4
        if img2img:
            out["num_inference_steps"] = max(1, min(50, steps))
        else:
            out["num_inference_steps"] = max(1, min(12, steps))
        try:
            cfg = float(payload.get("cfg_scale") or payload.get("guidance_scale") or 3.5)
        except (TypeError, ValueError):
            cfg = 3.5
        out["guidance_scale"] = max(1.0, min(20.0, cfg))
        seed = payload.get("seed")
        try:
            seed_i = int(seed)
            if seed_i >= 0:
                out["seed"] = seed_i
        except (TypeError, ValueError):
            pass
        if img2img:
            out["image_url"] = _forge_init_to_data_uri(payload)
            try:
                strength = float(payload.get("denoising_strength") or payload.get("strength") or 0.75)
            except (TypeError, ValueError):
                strength = 0.75
            out["strength"] = max(0.01, min(1.0, strength))
        return out

    def _to_forge_result(self, data: Dict[str, Any], path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        images = _fal_result_images_to_b64(data)
        if not images:
            raise BackendError(f"fal returned no images: {str(data)[:240]}", status=502)
        return {
            "images": images,
            "parameters": {},
            "info": json.dumps(
                {
                    "cloud_inference": "fal",
                    "path": path,
                    "model": self.IMG2IMG_MODEL if "img2img" in (path or "") else self.TXT2IMG_MODEL,
                    "seed": payload.get("seed", -1),
                }
            ),
        }

    def _http_json(
        self,
        method: str,
        url: str,
        payload: Optional[Dict[str, Any]],
        timeout_s: float,
    ) -> Tuple[int, bytes, str]:
        body = None if payload is None else json.dumps(payload).encode("utf-8")
        headers = self._auth_headers()
        if body is None:
            headers.pop("Content-Type", None)
        parsed = urllib.parse.urlparse(url)
        host = parsed.hostname or "queue.fal.run"
        port = parsed.port or (443 if parsed.scheme == "https" else 80)
        req_path = parsed.path or "/"
        if parsed.query:
            req_path = req_path + "?" + parsed.query
        if body is not None:
            headers["Content-Length"] = str(len(body))

        with self._io_lock:
            if self._aborted:
                raise BackendError("interrupted", status=499)
            epoch = self._gen_epoch

        conn: Any = None
        try:
            if parsed.scheme == "https":
                conn = http.client.HTTPSConnection(host, port, timeout=timeout_s)
            else:
                conn = http.client.HTTPConnection(host, port, timeout=timeout_s)
            with self._io_lock:
                if self._gen_epoch != epoch:
                    raise BackendError("interrupted", status=499)
                self._active_conn = conn
            conn.request(method.upper(), req_path, body=body, headers=headers)
            resp = conn.getresponse()
            with self._io_lock:
                if self._gen_epoch != epoch:
                    raise BackendError("interrupted", status=499)
            raw = resp.read()
            ct = resp.getheader("Content-Type") or "application/json"
            return int(resp.status), raw, ct
        except BackendError:
            raise
        except Exception as exc:
            with self._io_lock:
                aborted = bool(self._aborted) or self._gen_epoch != epoch
            if aborted:
                raise BackendError("interrupted", status=499) from exc
            raise BackendError(f"fal request failed: {exc}", status=502) from exc
        finally:
            with self._io_lock:
                if self._active_conn is conn:
                    self._active_conn = None
            if conn is not None:
                try:
                    conn.close()
                except Exception:
                    pass


def build_backend(mode: str, credential: str) -> CloudBackend:
    mode_n = (mode or "demo").strip().lower()
    cred = (credential or "").strip().strip("\"'").strip()
    if mode_n in ("demo", "local_demo"):
        return DemoBackend()
    if mode_n in ("remote_forge", "remote", "colab", "runpod", "tunnel"):
        return RemoteForgeBackend(cred)
    if mode_n in ("fal", "fal.ai"):
        return FalBackend(cred)
    raise BackendError(f"Unknown backend mode: {mode}", status=400)
