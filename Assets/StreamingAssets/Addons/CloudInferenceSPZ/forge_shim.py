"""Local Forge-compatible HTTP shim for CloudInferenceSPZ (127.0.0.1:7860)."""

from __future__ import annotations

import json
import socket
import threading
import time
import traceback
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
                "listen": f"{DEFAULT_HOST}:{DEFAULT_PORT}",
            }


_STATE = ForgeShimState()
_SERVER: Optional[ThreadingHTTPServer] = None
_THREAD: Optional[threading.Thread] = None
_SERVER_LOCK = threading.Lock()


def get_state() -> ForgeShimState:
    return _STATE


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


def _json_bytes(obj: Any) -> bytes:
    return json.dumps(obj).encode("utf-8")


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
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        try:
            if path in ("/internal/ping", "/ping"):
                self._send_json(200, {"status": "ok", "cloud_inference": True})
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
                st = get_state()
                with st.lock:
                    prog = float(st.progress)
                    active = bool(st.job_active)
                self._send_json(
                    200,
                    {
                        "progress": prog,
                        "eta_relative": max(0.0, (1.0 - prog) * 2.0),
                        "state": {"job": "cloud" if active else "", "job_count": 1 if active else 0},
                        "current_image": None,
                        "textinfo": "Cloud Inference" if active else None,
                    },
                )
                return
            if path == "/sdapi/v1/options":
                st = get_state()
                with st.lock:
                    opts = dict(st.options)
                self._send_json(200, opts)
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

            # Proxy GETs to remote Forge when configured.
            st = get_state()
            backend = st.backend
            if backend.name == "remote_forge":
                status, body, ct = backend.proxy("GET", path, None, dict(self.headers.items()))
                self._send(status, body, ct)
                return

            self._send_json(404, {"detail": f"shim has no GET {path}"})
        except BackendError as exc:
            self._send_json(exc.status, {"detail": str(exc)})
        except Exception as exc:
            print(f"[CloudInferenceSPZ] GET {path} failed: {exc}\n{traceback.format_exc()}")
            self._send_json(500, {"detail": str(exc)})

    def do_POST(self) -> None:  # noqa: N802
        path = urlparse(self.path).path
        try:
            if path == "/sdapi/v1/interrupt":
                st = get_state()
                with st.lock:
                    st.interrupt = True
                    st.job_active = False
                    st.progress = 0.0
                self._send_json(200, {"interrupted": True})
                return

            if path == "/sdapi/v1/options":
                payload = self._read_json()
                st = get_state()
                with st.lock:
                    st.options.update(payload)
                    opts = dict(st.options)
                self._send_json(200, opts)
                return

            if path in ("/sdapi/v1/txt2img", "/sdapi/v1/img2img"):
                payload = self._read_json()
                st = get_state()
                with st.lock:
                    st.job_active = True
                    st.interrupt = False
                    st.progress = 0.05
                    st.started_at = time.time()
                    backend = st.backend
                # Fake progress ticks for SPZ ETA UI while backend runs.
                def _tick() -> None:
                    for p in (0.2, 0.45, 0.7, 0.9):
                        time.sleep(0.05)
                        with st.lock:
                            if st.interrupt:
                                return
                            st.progress = p

                ticker = threading.Thread(target=_tick, daemon=True)
                ticker.start()
                try:
                    result = backend.generate(path, payload)
                except BackendError as exc:
                    with st.lock:
                        st.job_active = False
                        st.progress = 0.0
                        st.last_error = str(exc)
                    self._send_json(exc.status, {"detail": str(exc), "error": str(exc)})
                    return
                except Exception as exc:
                    with st.lock:
                        st.job_active = False
                        st.progress = 0.0
                        st.last_error = str(exc)
                    raise
                ticker.join(timeout=2.0)
                with st.lock:
                    st.job_active = False
                    st.progress = 1.0
                    st.last_error = ""
                self._send_json(200, result)
                return

            if path == "/controlnet/detect":
                # Echo-friendly stub: return empty images so callers fail soft.
                self._send_json(200, {"images": [], "info": "cloud shim detect stub (T5 pending)"})
                return

            st = get_state()
            if st.backend.name == "remote_forge":
                length = int(self.headers.get("Content-Length") or 0)
                body = self.rfile.read(length) if length > 0 else None
                status, raw, ct = st.backend.proxy("POST", path, body, dict(self.headers.items()))
                self._send(status, raw, ct)
                return

            self._send_json(404, {"detail": f"shim has no POST {path}"})
        except BackendError as exc:
            self._send_json(exc.status, {"detail": str(exc)})
        except Exception as exc:
            print(f"[CloudInferenceSPZ] POST {path} failed: {exc}\n{traceback.format_exc()}")
            self._send_json(500, {"detail": str(exc)})


def start_shim(host: str = DEFAULT_HOST, port: int = DEFAULT_PORT) -> Tuple[bool, str]:
    global _SERVER, _THREAD
    with _SERVER_LOCK:
        if _SERVER is not None:
            return True, f"already listening on {host}:{port}"
        if not is_port_free(host, port):
            return False, (
                f"port {host}:{port} is already in use — stop local Forge/WebUI "
                "or disconnect whatever owns :7860, then Connect again"
            )
        try:
            server = ThreadingHTTPServer((host, port), _Handler)
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
        thread.start()
        return True, f"listening on http://{host}:{port}"


def stop_shim() -> Tuple[bool, str]:
    global _SERVER, _THREAD
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
    st = get_state()
    with st.lock:
        st.job_active = False
        st.progress = 0.0
    return True, "shim stopped"
