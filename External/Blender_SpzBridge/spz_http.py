"""
REST client for StableProjectorz HTTP API (default http://127.0.0.1:5557).
Stdlib only (urllib + json) — no pip dependencies.

Headless mesh I/O (no Unity file dialogs) maps to the same ``spz.cmd`` as the
in-app StableProjectorz GO add-on (``Assets/StreamingAssets/Addons/StableProjectorzGO/``):
  ``POST /api/v1/meshes/import``  /  ``POST /api/v1/export/3d_to_path``
"""

import json
import urllib.error
import urllib.parse
import urllib.request
from typing import Any, Optional


class SpzHttpError(Exception):
    def __init__(self, message: str, status: Optional[int] = None, body: Optional[str] = None):
        super().__init__(message)
        self.status = status
        self.body = body

    def __str__(self) -> str:
        s = str(self.args[0]) if self.args else ""
        if self.status is not None:
            s = f"[HTTP {self.status}] {s}"
        if self.body:
            s += f"\n{self.body[:2000]}"
        return s


def request_json(
    base_url: str,
    method: str,
    path: str,
    body: Optional[dict] = None,
    timeout_s: float = 120.0,
) -> Any:
    url = base_url.rstrip("/") + path
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, method=method.upper(), headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=timeout_s) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            if not raw:
                return None
            try:
                return json.loads(raw)
            except json.JSONDecodeError as e:
                raise SpzHttpError(f"Invalid JSON from {path}: {e}", status=None) from e
    except urllib.error.HTTPError as e:
        try:
            body_txt = e.read().decode("utf-8", errors="replace")
        except Exception:
            body_txt = ""
        raise SpzHttpError(str(e), status=e.code, body=body_txt) from e
    except urllib.error.URLError as e:
        # Connection refused, DNS, SSL, etc. (HTTPError is a subclass, already handled above)
        r = e.reason
        msg = f"{e}"
        if r is not None and str(r) and str(r) not in msg:
            msg = f"{e} ({r})"
        raise SpzHttpError(msg, status=None) from e


def get_project_info(base_url: str) -> dict:
    return request_json(base_url, "GET", "/api/v1/project/info")


def post_export_3d_with_textures(base_url: str) -> dict:
    # Dialog + texture pipeline is a Unity long op (~300s budget); 120s default timed
    # out while Unity was still writing (false failure in Blender).
    return request_json(base_url, "POST", "/api/v1/export/3d_with_textures", {}, timeout_s=300.0)


def post_mesh_stream(
    base_url: str,
    host: str = "127.0.0.1",
    port: int = 5560,
    codec: str = "gzip",
) -> dict:
    """Ask SPZ to push geometry to this Blender add-on's loopback listener."""
    # Encode + TCP push can exceed 30s on large meshes; match Unity long-op budget (~300s)
    # so a late ACK cannot arrive after Blender already fell back to FBX (duplicate geometry).
    return request_json(
        base_url,
        "POST",
        "/api/v1/export/mesh_stream",
        {"host": str(host), "port": int(port), "codec": str(codec)},
        timeout_s=300.0,
    )


def post_export_projection_textures(base_url: str, is_dilate: bool = True) -> dict:
    q = urllib.parse.urlencode({"is_dilate": "true" if is_dilate else "false"})
    return request_json(base_url, "POST", f"/api/v1/export/projection_textures?{q}", {}, timeout_s=300.0)


def post_export_view_textures(base_url: str) -> dict:
    return request_json(base_url, "POST", "/api/v1/export/view_textures", {}, timeout_s=300.0)


def post_import_3d_model(base_url: str, filepath: str) -> dict:
    # Assimp + UDIM scan can exceed the default 120s; match Unity long-op budget (~300s).
    return request_json(
        base_url,
        "POST",
        "/api/v1/meshes/import",
        {"filepath": str(filepath)},
        timeout_s=300.0,
    )


def post_export_3d_to_path(base_url: str, mesh_filepath: str) -> dict:
    # Texture dilate/encode can exceed the default 120s; must match Unity long-op budget (~300s).
    return request_json(
        base_url,
        "POST",
        "/api/v1/export/3d_to_path",
        {"mesh_filepath": str(mesh_filepath)},
        timeout_s=300.0,
    )


def get_meshes(base_url: str) -> dict:
    return request_json(base_url, "GET", "/api/v1/meshes")


def get_context(base_url: str) -> dict:
    return request_json(base_url, "GET", "/api/v1/context")
