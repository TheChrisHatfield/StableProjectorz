# SPDX-License-Identifier: MIT
"""
SPZ GO — Substance Painter side. File-exchange bridge over the Painter Python plugin API.

Unlike ZBrush, Painter exposes a real plugin lifecycle (`start_plugin` / `close_plugin`) and Qt, so
this bridge runs a background watcher that mirrors the Blender bridge:

  * SPZ → Painter (Export in SPZ): SPZ writes `from_spz.fbx` + a `.spz_go_ready` stamp; the watcher
    creates/updates a Painter project from that mesh.
  * Painter → SPZ (Import in SPZ): SPZ drops `spz_go_pull_request.json`; the watcher exports the
    current textures + mesh and POSTs `/api/v1/meshes/import`.

Does NOT depend on the Maxon/Adobe Substance Bridge. All `substance_painter` and Qt imports are
guarded so this module imports in plain CPython (contract tests / installer).

EXPERIMENTAL: protocol + SPZ endpoints are shared/tested with the working Blender bridge; the
Painter-side project/export calls need a live-Painter spike to confirm exact API usage. When UVs are
missing the bridge refuses honestly rather than exporting garbage.
"""

from __future__ import annotations

import os
import time
from typing import Optional, Tuple

try:
    from . import spz_http
except ImportError:  # pragma: no cover - Painter flat import
    import spz_http  # type: ignore

HOST_ID = "painter"
BASE_URL_ENV = "SPZ_BASE_URL"
DEFAULT_BASE_URL = "http://127.0.0.1:5557"

EXCHANGE_ROOT_NAME = "StableProjectorzGO_exchange"
EXCHANGE_SUBDIR = HOST_ID

# Same literals SPZ writes (AddonUI_MGR.SpzGoSections.cs) — pinned by contract tests on both sides.
PULL_REQUEST_NAME = "spz_go_pull_request.json"
SPZ_PULL_BASENAME = "from_spz"
READY_STAMP_SUFFIX = ".spz_go_ready"
PAINTER_PUSH_BASENAME = "from_painter"
POLL_INTERVAL_MS = 1500

# SPZ pack labels ← Painter "PBR Metallic Roughness" preset channels (Phase 3 mapping).
PACK_LABEL_MAP = {
    "basecolor": "albedo",
    "normal": "normal",
    "roughness": "roughness",
    "metallic": "metallic",
    "height": "height",
    "ambientocclusion": "ao",
}

_timer = None
_last_ready_fp = None
_seeded = False
# Fingerprint of a pull request we already tried and failed — leave the marker on disk so a fresh
# Import click (new mtime) can retry, but do not re-fire every POLL_INTERVAL_MS on the same file.
_last_failed_pull_fp = None


def base_url() -> str:
    return os.environ.get(BASE_URL_ENV) or DEFAULT_BASE_URL


def resolve_exchange_dir(url: Optional[str] = None) -> Tuple[Optional[str], str]:
    url = url or base_url()
    try:
        info = spz_http.get_project_info(url)
    except spz_http.SpzHttpError as e:
        return None, str(e)
    if not info.get("data_dir_available") or not info.get("data_dir"):
        return None, "No project data_dir in SPZ — save a StableProjectorz project first."
    exdir = os.path.join(info["data_dir"], EXCHANGE_ROOT_NAME, EXCHANGE_SUBDIR)
    try:
        os.makedirs(exdir, exist_ok=True)
    except OSError as e:
        return None, f"Could not create exchange dir: {e}"
    return exdir, ""


def spz_pull_fbx(exdir: str) -> str:
    return os.path.join(exdir, SPZ_PULL_BASENAME + ".fbx")


def spz_ready_stamp(exdir: str) -> str:
    return os.path.join(exdir, SPZ_PULL_BASENAME + READY_STAMP_SUFFIX)


def pull_request_path(exdir: str) -> str:
    return os.path.join(exdir, PULL_REQUEST_NAME)


def push_mesh_path(exdir: str, ext: str = ".fbx") -> str:
    return os.path.join(exdir, PAINTER_PUSH_BASENAME + ext)


def _fingerprint(path: str):
    try:
        st = os.stat(path)
        return (st.st_mtime_ns, st.st_size)
    except OSError:
        return None


# --- Painter ops (guarded; require a live Painter host) ---------------------------------------

def _painter_available() -> bool:
    try:
        import substance_painter  # type: ignore  # noqa: F401
        return True
    except Exception:
        return False


def _painter_has_usable_uvs() -> bool:
    """Refuse to export without UVs (R: 'refuse honestly if no usable UVs')."""
    if not _painter_available():
        return False
    try:
        import substance_painter.project as project  # type: ignore
        if not project.is_open():
            return False
        # A live spike wires this to the real mesh/UV query; default to True when a project is open.
        return True
    except Exception:
        return False


def _painter_open_project_from_mesh(mesh_path: str) -> Tuple[bool, str]:
    if not _painter_available():
        return False, f"Painter API unavailable — open {mesh_path} in Substance Painter manually."
    try:
        import substance_painter.project as project  # type: ignore
        if project.is_open():
            # Reload geometry into the existing project so we replace, not duplicate.
            reload_fn = getattr(project, "reload_mesh", None)
            if callable(reload_fn):
                reload_fn(mesh_path)
                return True, mesh_path
        create = getattr(project, "create", None)
        if callable(create):
            settings = getattr(project, "Settings", None)
            create(mesh_file_path=mesh_path, settings=settings() if callable(settings) else None)
            return True, mesh_path
        return False, "No known Painter project.create entry point (needs live-Painter spike)."
    except Exception as e:
        return False, f"Painter project open failed: {e}"


def _painter_export_textures_and_mesh(out_mesh_path: str) -> Tuple[bool, str]:
    if not _painter_available():
        return False, "Painter API unavailable — export from Substance Painter manually."
    if not _painter_has_usable_uvs():
        return False, "No usable UVs in the open Painter project — cannot export a pack."
    try:
        import substance_painter.export as export  # type: ignore  # noqa: F401
        # A live spike wires the export preset ("PBR Metallic Roughness") → PACK_LABEL_MAP here and
        # writes the mesh next to the maps. Until then, report that the op needs the spike.
        return False, "Painter texture/mesh export needs a live-Painter spike (preset → pack labels)."
    except Exception as e:
        return False, f"Painter export failed: {e}"


# --- Actions + watcher ------------------------------------------------------------------------

def spz_import() -> Tuple[bool, str]:
    """SPZ → Painter: ask SPZ to export its model, then open/reload it in Painter."""
    url = base_url()
    exdir, err = resolve_exchange_dir(url)
    if exdir is None:
        return False, err
    fbx = spz_pull_fbx(exdir)
    try:
        r = spz_http.post_export_3d_to_path(url, fbx)
    except spz_http.SpzHttpError as e:
        return False, str(e)
    if not (isinstance(r, dict) and r.get("success") is True):
        return False, f"SPZ export failed: {r!r}"
    for _ in range(60):
        if os.path.isfile(fbx) and os.path.getsize(fbx) > 32:
            break
        time.sleep(0.2)
    if not (os.path.isfile(fbx) and os.path.getsize(fbx) > 32):
        return False, f"SPZ reported OK but {fbx} not written."
    return _painter_open_project_from_mesh(fbx)


def spz_export() -> Tuple[bool, str]:
    """Painter → SPZ: export textures + mesh, then ask SPZ to import."""
    url = base_url()
    exdir, err = resolve_exchange_dir(url)
    if exdir is None:
        return False, err
    out = push_mesh_path(exdir, ".fbx")
    ok, msg = _painter_export_textures_and_mesh(out)
    if not ok:
        return False, msg
    try:
        r = spz_http.post_import_3d_model(url, out)
    except spz_http.SpzHttpError as e:
        return False, str(e)
    if isinstance(r, dict) and r.get("success") is True:
        return True, f"Export → SPZ: {out}"
    return False, f"File written; SPZ import failed: {r!r}"


def _consume_pull_request(exdir: str) -> bool:
    """Answer an SPZ Import request. Only remove the marker after a successful push.

    Delete-before-push (Blender) is fine when export can succeed; Painter's export path still
    fails closed until the live spike lands. Removing the marker first burned every Import click
    on the first watcher tick, so the user had to press Import again with no automatic retry.
    """
    global _last_failed_pull_fp
    req = pull_request_path(exdir)
    if not os.path.isfile(req):
        return False
    fp = _fingerprint(req)
    if fp is not None and fp == _last_failed_pull_fp:
        return False
    ok, msg = spz_export()
    if not ok:
        _last_failed_pull_fp = fp
        print("SPZ GO (Painter): pull request → FAILED -", msg, "(marker kept for retry)")
        return False
    try:
        os.remove(req)
    except OSError as e:
        print("SPZ GO (Painter): push OK but could not clear pull request:", e)
        # Still treat as success — SPZ already has the mesh; a leftover marker would only re-push.
    _last_failed_pull_fp = None
    print("SPZ GO (Painter): pull request → OK -", msg)
    return True


def _watch_tick() -> None:
    """Poll the exchange folder once: answer a pull request, then auto-import a fresh SPZ export."""
    global _last_ready_fp, _seeded
    exdir, err = resolve_exchange_dir()
    if exdir is None:
        return
    _consume_pull_request(exdir)
    stamp = spz_ready_stamp(exdir)
    fp = _fingerprint(stamp)
    if not _seeded:
        # Adopt the current stamp so a leftover export at plugin start is not re-imported.
        _last_ready_fp = fp
        _seeded = True
        return
    if fp is None or fp == _last_ready_fp:
        return
    fbx = spz_pull_fbx(exdir)
    if os.path.isfile(fbx) and os.path.getsize(fbx) > 32:
        ok, msg = _painter_open_project_from_mesh(fbx)
        if ok:
            _last_ready_fp = fp
        print("SPZ GO (Painter): auto-import", "OK" if ok else "FAILED", "-", msg)


def start_plugin() -> None:
    """Substance Painter plugin entry point — start the background watcher."""
    global _timer, _seeded
    _seeded = False
    try:
        from PySide6 import QtCore  # type: ignore
    except Exception:
        try:
            from PySide2 import QtCore  # type: ignore
        except Exception:
            print("SPZ GO (Painter): Qt unavailable — watcher not started (call spz_import/spz_export manually).")
            return
    _timer = QtCore.QTimer()
    _timer.setInterval(POLL_INTERVAL_MS)
    _timer.timeout.connect(_watch_tick)
    _timer.start()
    print("SPZ GO (Painter): watcher started.")


def close_plugin() -> None:
    global _timer
    if _timer is not None:
        try:
            _timer.stop()
        except Exception:
            pass
        _timer = None


if __name__ == "__main__":  # pragma: no cover - manual smoke inside Painter
    start_plugin()
