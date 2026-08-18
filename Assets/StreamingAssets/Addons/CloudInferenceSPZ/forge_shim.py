"""Local Forge-compatible HTTP shim for CloudInferenceSPZ (127.0.0.1:7860)."""

from __future__ import annotations

import json
import socket
import threading
import time
import traceback
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, Optional, Tuple
from urllib.parse import urlparse

from backends import BackendError, CloudBackend, DemoBackend


DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 7860


class ForgeShimState:
    def __init__(self) -> None:
        self.lock = threading.RLock()
        self.backend: CloudBackend = DemoBackend()
        self.progress: float = 0.0
        self.job_active: bool = False
        self.interrupt: bool = False
        self.last_error: str = ""
        self.started_at: float = 0.0
        self.options: Dict[str, Any] = {
            "sd_model_checkpoint": "cloud-inference-demo",
            "sd_vae": "Automatic",
        }

    def set_backend(self, backend: CloudBackend) -> None:
        with self.lock:
            self.backend = backend
            self.last_error = ""

    def snapshot_status(self) -> Dict[str, Any]:
        with self.lock:
            return {
                "backend": self.backend.describe(),
                "job_active": self.job_active,
                "progress": self.progress,
                "last_error": self.last_error,
                "listen": listen_endpoint(),
            }


_STATE = ForgeShimState()
_SERVER: Optional[ThreadingHTTPServer] = None
_THREAD: Optional[threading.Thread] = None
_SERVER_LOCK = threading.Lock()
_LISTEN_HOST = DEFAULT_HOST
_LISTEN_PORT = DEFAULT_PORT


class _ReuseThreadingHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = True


def get_state() -> ForgeShimState:
    return _STATE


def listen_endpoint() -> str:
    return f"{_LISTEN_HOST}:{_LISTEN_PORT}"


def is_port_free(host: str = DEFAULT_HOST, port: int = DEFAULT_PORT) -> bool:
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        sock.settimeout(0.4)
        return sock.connect_ex((host, port)) != 0
    finally:
        sock.close()


def is_running() -> bool:
    with _SERVER_LOCK:
        return _SERVER is not None


def ping_listen(timeout_s: float = 2.0) -> Tuple[bool, str]:
    """Confirm OUR shim answers on listen_endpoint (not an unrelated Forge on :7860)."""
    hostport = listen_endpoint()
    try:
        with urllib.request.urlopen(f"http://{hostport}/internal/ping", timeout=timeout_s) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            if int(resp.status) != 200:
                return False, raw or f"HTTP {resp.status}"
            try:
                data = json.loads(raw) if raw else {}
            except Exception:
                return False, f"non-JSON ping body: {raw[:120]}"
            if not (isinstance(data, dict) and data.get("cloud_inference") is True):
                return False, (
                    f"{hostport} answered but is not Cloud Inference shim "
                    "(stop local Forge/WebUI, then Connect again)"
                )
            return True, raw
    except Exception as exc:
        return False, str(exc)


def _shim_ping_ok(host: str, port: int, timeout_s: float = 1.0) -> bool:
    """True only if OUR shim answers (body includes cloud_inference), not a real Forge."""
    try:
        with urllib.request.urlopen(f"http://{host}:{port}/internal/ping", timeout=timeout_s) as resp:
            if int(resp.status) != 200:
                return False
            raw = resp.read().decode("utf-8", errors="replace")
            try:
                data = json.loads(raw) if raw else {}
            except Exception:
                return False
            return isinstance(data, dict) and data.get("cloud_inference") is True
    except Exception:
        return False


def _json_bytes(obj: Any) -> bytes:
    return json.dumps(obj).encode("utf-8")


def _safe_int_dim(val: Any, default: int = 64) -> int:
    try:
        if val is None or val == "":
            return default
        return max(8, min(2048, int(float(val))))
    except (TypeError, ValueError):
        return default


def _demo_catalog_get(path: str) -> Optional[Tuple[bytes, str]]:
    """Return (body, content_type) for Forge list endpoints used by SPZ dropdowns."""
    if path == "/sdapi/v1/sd-models":
        return (
            _json_bytes(
                [
                    {
                        "title": "cloud-inference-demo [cloud]",
                        "model_name": "cloud-inference-demo",
                        "hash": "cloud",
                        "sha256": "",
                        "filename": "cloud-inference-demo.safetensors",
                    }
                ]
            ),
            "application/json",
        )
    if path == "/sdapi/v1/samplers":
        return (
            _json_bytes(
                [
                    {"name": "Euler a", "aliases": ["k_euler_a"], "options": {}},
                    {"name": "Euler", "aliases": ["k_euler"], "options": {}},
                    {"name": "DDIM", "aliases": [], "options": {}},
                ]
            ),
            "application/json",
        )
    if path == "/sdapi/v1/schedulers":
        return (
            _json_bytes(
                [
                    {
                        "name": "automatic",
                        "label": "Automatic",
                        "aliases": [],
                        "default_rho": 1.0,
                        "need_inner_model": False,
                    },
                    {
                        "name": "normal",
                        "label": "Normal",
                        "aliases": [],
                        "default_rho": 1.0,
                        "need_inner_model": False,
                    },
                ]
            ),
            "application/json",
        )
    if path == "/sdapi/v1/upscalers":
        return (
            _json_bytes(
                [
                    {
                        "name": "None",
                        "model_name": None,
                        "model_path": None,
                        "model_url": None,
                        "scale": 4.0,
                    },
                    {
                        "name": "Lanczos",
                        "model_name": None,
                        "model_path": None,
                        "model_url": None,
                        "scale": 4.0,
                    },
                ]
            ),
            "application/json",
        )
    if path in ("/sdapi/v1/sd-vae", "/sdapi/v1/sd-modules"):
        return (
            _json_bytes(
                [
                    {"model_name": "Automatic", "filename": None},
                    {"model_name": "None", "filename": None},
                ]
            ),
            "application/json",
        )
    return None


class _Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt: str, *args: Any) -> None:
        # Keep Unity console quieter; errors still print via print().
        return

    def _read_json(self) -> Dict[str, Any]:
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length > 0 else b"{}"
        if not raw:
            return {}
        try:
            data = json.loads(raw.decode("utf-8"))
        except Exception:
            return {}
        return data if isinstance(data, dict) else {}

    def _send(self, status: int, body: bytes, content_type: str = "application/json") -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        # UnityWebRequest + HTTP/1.1 keep-alive can stall ping/progress polls on this shim.
        self.send_header("Connection", "close")
        self.close_connection = True
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _send_json(self, status: int, obj: Any) -> None:
        self._send(status, _json_bytes(obj))

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization")
        self.send_header("Connection", "close")
        self.close_connection = True
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        full_path = self.path  # keep query string for upstream ControlNet (?update=true)
        try:
            st = get_state()
            with st.lock:
                backend = st.backend
            is_remote = backend.name == "remote_forge"

            if path in ("/internal/ping", "/ping"):
                self._send_json(200, {"status": "ok", "cloud_inference": True})
                return

            # Remote Forge: proxy options/sysinfo/ControlNet so SPZ sees upstream catalogs.
            if is_remote and path in (
                "/internal/sysinfo",
                "/sdapi/v1/options",
                "/controlnet/model_list",
                "/controlnet/module_list",
                "/controlnet/control_types",
                "/controlnet/settings",
            ):
                status, body, ct = backend.proxy("GET", full_path, None, dict(self.headers.items()))
                self._send(status, body, ct)
                return

            if path == "/internal/sysinfo":
                # Keys must match SD_SysInfo JsonProperty names ("Data path", "Script path").
                # Path/Version must look Forge-family so isForgeWebui_detected() is true.
                self._send_json(
                    200,
                    {
                        "Platform": "cloud-inference-shim",
                        "Python": "3",
                        "Version": "neo cloud-inference",
                        "Data path": "CloudInferenceSPZ/forge-shim",
                        "Script path": "CloudInferenceSPZ/forge-shim",
                        "Config": {
                            "control_net_unit_count": 3,
                            "control_net_max_models_num": 3,
                            "control_net_model_cache_size": 1,
                        },
                    },
                )
                return
            if path == "/sdapi/v1/progress":
                with st.lock:
                    active = bool(st.job_active)
                    # Idle Forge returns ~0; do not leave sticky 1.0 after a finished job.
                    prog = float(st.progress) if active else 0.0
                self._send_json(
                    200,
                    {
                        "progress": prog,
                        "eta_relative": max(0.0, (1.0 - prog) * 2.0) if active else 0.0,
                        "state": {"job": "cloud" if active else "", "job_count": 1 if active else 0},
                        "current_image": None,
                        "textinfo": "Cloud Inference" if active else None,
                    },
                )
                return
            if path == "/sdapi/v1/options":
                with st.lock:
                    opts = dict(st.options)
                self._send_json(200, opts)
                return

            # Demo catalog stubs only when NOT proxying to remote Forge.
            if not is_remote:
                catalog = _demo_catalog_get(path)
                if catalog is not None:
                    self._send(200, catalog[0], catalog[1])
                    return

                if path in (
                    "/controlnet/model_list",
                    "/controlnet/module_list",
                    "/controlnet/control_types",
                    "/controlnet/settings",
                ):
                    # Soft stubs so catalog polls do not hard-fail; real CN is T5.
                    if path.endswith("model_list"):
                        self._send_json(200, {"model_list": ["None"]})
                    elif path.endswith("module_list"):
                        self._send_json(200, {"module_list": ["none"]})
                    elif path.endswith("control_types"):
                        self._send_json(200, {"control_types": {}})
                    else:
                        self._send_json(404, {"detail": "controlnet settings not available on cloud shim"})
                    return

            # Proxy remaining GETs to remote Forge when configured (incl. sd-models/samplers/VAE).
            if is_remote:
                status, body, ct = backend.proxy("GET", full_path, None, dict(self.headers.items()))
                self._send(status, body, ct)
                return

            self._send_json(404, {"detail": f"shim has no GET {path}"})
        except BackendError as exc:
            self._send_json(exc.status, {"detail": str(exc)})
        except Exception as exc:
            print(f"[CloudInferenceSPZ] GET {path} failed: {exc}\n{traceback.format_exc()}")
            self._send_json(500, {"detail": str(exc)})

    def _dispatch_generate(self, st: ForgeShimState, backend: CloudBackend, path: str, payload: Dict[str, Any]) -> None:
        with st.lock:
            st.job_active = True
            st.interrupt = False
            st.progress = 0.05
            st.started_at = time.time()
            backend = st.backend
        begin = getattr(backend, "begin_job", None)
        if callable(begin):
            begin()
        done_ev = threading.Event()
        box: Dict[str, Any] = {}

        def _tick() -> None:
            p = 0.05
            while not done_ev.wait(0.4):
                with st.lock:
                    if st.interrupt:
                        return
                    p = min(0.95, p + 0.03)
                    st.progress = p

        def _worker() -> None:
            try:
                box["result"] = backend.generate(path, payload)
            except Exception as exc:
                box["exc"] = exc
            finally:
                done_ev.set()

        ticker = threading.Thread(target=_tick, daemon=True)
        worker = threading.Thread(target=_worker, daemon=True)
        ticker.start()
        worker.start()
        while not done_ev.wait(0.1):
            with st.lock:
                interrupted = bool(st.interrupt)
            if interrupted:
                abort = getattr(backend, "abort", None)
                if callable(abort):
                    try:
                        abort()
                    except Exception:
                        pass
                done_ev.wait(1.0)
                with st.lock:
                    st.job_active = False
                    st.progress = 0.0
                    st.last_error = ""
                self._send_json(200, {"images": [], "interrupted": True})
                return

        ticker.join(timeout=1.0)
        if "exc" in box:
            exc = box["exc"]
            with st.lock:
                interrupted = bool(st.interrupt)
                st.job_active = False
                st.progress = 0.0
                st.last_error = "" if interrupted else str(exc)
            if interrupted:
                self._send_json(200, {"images": [], "interrupted": True, "info": str(exc)})
                return
            if isinstance(exc, BackendError):
                self._send_json(exc.status, {"detail": str(exc), "error": str(exc)})
                return
            raise exc
        result = box.get("result") or {}
        with st.lock:
            interrupted = bool(st.interrupt)
            st.job_active = False
            st.progress = 0.0 if interrupted else 1.0
            st.last_error = ""
        if interrupted:
            self._send_json(200, {"images": [], "interrupted": True})
            return
        self._send_json(200, result)

    def do_POST(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        full_path = self.path
        try:
            st = get_state()
            with st.lock:
                backend = st.backend
            is_remote = backend.name == "remote_forge"

            if path == "/sdapi/v1/interrupt":
                with st.lock:
                    st.interrupt = True
                    st.job_active = False
                    st.progress = 0.0
                    backend = st.backend
                    is_remote = backend.name == "remote_forge"
                abort = getattr(backend, "abort", None)
                if callable(abort):
                    try:
                        abort()
                    except Exception:
                        pass
                if is_remote:
                    try:
                        length = int(self.headers.get("Content-Length") or 0)
                        body = self.rfile.read(length) if length > 0 else b"{}"
                        backend.proxy("POST", full_path, body or b"{}", dict(self.headers.items()))
                    except Exception:
                        pass
                self._send_json(200, {"interrupted": True})
                return

            if path == "/sdapi/v1/unload-checkpoint":
                if is_remote:
                    length = int(self.headers.get("Content-Length") or 0)
                    body = self.rfile.read(length) if length > 0 else b"{}"
                    status, raw, ct = backend.proxy("POST", full_path, body, dict(self.headers.items()))
                    self._send(status, raw, ct)
                    return
                self._send_json(200, {})
                return

            if path == "/sdapi/v1/options":
                if is_remote:
                    length = int(self.headers.get("Content-Length") or 0)
                    body = self.rfile.read(length) if length > 0 else b"{}"
                    status, raw, ct = backend.proxy("POST", full_path, body, dict(self.headers.items()))
                    self._send(status, raw, ct)
                    return
                payload = self._read_json()
                with st.lock:
                    st.options.update(payload)
                    opts = dict(st.options)
                self._send_json(200, opts)
                return

            if path in ("/sdapi/v1/txt2img", "/sdapi/v1/img2img"):
                payload = self._read_json()
                self._dispatch_generate(st, backend, path, payload)
                return

            if path == "/sdapi/v1/extra-batch-images":
                # Upscale path: Demo returns a solid PNG; remote proxies (interruptible).
                payload = self._read_json()
                if is_remote:
                    self._dispatch_generate(st, backend, path, payload)
                    return
                w = _safe_int_dim(
                    payload.get("rslt_imageWidths")
                    or payload.get("resize_width")
                    or payload.get("upscaling_resize_w")
                    or payload.get("width"),
                    64,
                )
                h = _safe_int_dim(
                    payload.get("rslt_imageHeights")
                    or payload.get("resize_height")
                    or payload.get("upscaling_resize_h")
                    or payload.get("height"),
                    64,
                )
                if w == 64 and h == 64:
                    scale = _safe_int_dim(payload.get("upscaling_resize"), 1)
                    if scale > 1:
                        w = h = min(2048, 64 * scale)
                result = DemoBackend().generate("/sdapi/v1/txt2img", {"width": w, "height": h})
                self._send_json(200, result)
                return

            if path == "/controlnet/detect":
                if is_remote:
                    length = int(self.headers.get("Content-Length") or 0)
                    body = self.rfile.read(length) if length > 0 else None
                    status, raw, ct = backend.proxy("POST", full_path, body, dict(self.headers.items()))
                    self._send(status, raw, ct)
                    return
                payload = self._read_json()
                images = payload.get("controlnet_input_images") or payload.get("images") or []
                if not isinstance(images, list):
                    images = []
                # Echo inputs so SPZ detect callers (empty images[] = hard fail) stay wired.
                self._send_json(
                    200,
                    {
                        "images": images,
                        "info": "cloud shim detect echo (preprocessor T5 pending)",
                    },
                )
                return

            if is_remote:
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length) if length > 0 else None
                status, raw, ct = backend.proxy("POST", full_path, body, dict(self.headers.items()))
                self._send(status, raw, ct)
                return

            self._send_json(404, {"detail": f"shim has no POST {path}"})
        except BackendError as exc:
            self._send_json(exc.status, {"detail": str(exc)})
        except Exception as exc:
            print(f"[CloudInferenceSPZ] POST {path} failed: {exc}\n{traceback.format_exc()}")
            self._send_json(500, {"detail": str(exc)})


def start_shim(host: str = DEFAULT_HOST, port: int = DEFAULT_PORT) -> Tuple[bool, str]:
    global _SERVER, _THREAD, _LISTEN_HOST, _LISTEN_PORT

    # Health-check outside the lock so we never block request threads on _SERVER_LOCK.
    with _SERVER_LOCK:
        existing = _SERVER
        existing_thread = _THREAD
        existing_host, existing_port = _LISTEN_HOST, _LISTEN_PORT

    if existing is not None:
        if existing_thread is not None and existing_thread.is_alive() and _shim_ping_ok(existing_host, existing_port):
            return True, f"already listening on {existing_host}:{existing_port}"
        stop_shim()

    with _SERVER_LOCK:
        if _SERVER is not None:
            # Another connect won the race — treat as success if healthy.
            alive = _THREAD is not None and _THREAD.is_alive()
            cur_host, cur_port = _LISTEN_HOST, _LISTEN_PORT
        else:
            alive = False
            cur_host, cur_port = host, port

    if alive:
        if _shim_ping_ok(cur_host, cur_port):
            return True, f"already listening on {cur_host}:{cur_port}"
        stop_shim()

    with _SERVER_LOCK:
        if _SERVER is not None:
            return True, f"already listening on {_LISTEN_HOST}:{_LISTEN_PORT}"

        if not is_port_free(host, port):
            return False, (
                f"port {host}:{port} is already in use — stop local Forge/WebUI "
                "or disconnect whatever owns :7860, then Connect again"
            )
        try:
            server = _ReuseThreadingHTTPServer((host, port), _Handler)
            server.daemon_threads = True
        except OSError as exc:
            return False, f"bind failed on {host}:{port}: {exc}"

        def _serve() -> None:
            try:
                server.serve_forever(poll_interval=0.3)
            except Exception as exc:
                print(f"[CloudInferenceSPZ] shim server stopped: {exc}")

        thread = threading.Thread(target=_serve, name="SPZ-CloudInferenceShim", daemon=True)
        _SERVER = server
        _THREAD = thread
        _LISTEN_HOST = host
        _LISTEN_PORT = port
        thread.start()

    deadline = time.time() + 1.5
    while time.time() < deadline:
        if _shim_ping_ok(host, port, timeout_s=0.3):
            return True, f"listening on http://{host}:{port}"
        time.sleep(0.05)
    stop_shim()
    return False, f"bound {host}:{port} but ping did not become ready"


def stop_shim() -> Tuple[bool, str]:
    global _SERVER, _THREAD
    st = get_state()
    with st.lock:
        st.interrupt = True
        st.job_active = False
        st.progress = 0.0
        backend = st.backend
    abort = getattr(backend, "abort", None)
    if callable(abort):
        try:
            abort()
        except Exception:
            pass
    with _SERVER_LOCK:
        server = _SERVER
        _SERVER = None
        _THREAD = None
    if server is None:
        return True, "shim was not running"
    try:
        server.shutdown()
    except Exception as exc:
        return False, f"shutdown error: {exc}"
    try:
        server.server_close()
    except Exception:
        pass
    return True, "shim stopped"
