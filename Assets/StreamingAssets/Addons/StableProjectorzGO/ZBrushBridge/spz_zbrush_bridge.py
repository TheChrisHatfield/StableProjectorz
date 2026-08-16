# SPDX-License-Identifier: MIT
"""
SPZ GO — ZBrush side. File-exchange bridge (GoZ-style), *not* a Blender-style live mesh stream.

ZBrush's scripting host has no dependable always-on background timer, so this bridge is driven by
explicit palette buttons (registered under ZPlugin/ZScript) the user taps:

  spz_import()          Pull the current StableProjectorz model into ZBrush (SPZ writes, ZBrush loads)
  spz_export()          Push the active Tool/SubTool to StableProjectorz (ZBrush writes, SPZ imports)
  spz_poll_pull_request()  If SPZ pressed Import for ZBrush, run spz_export() once

Mesh I/O uses the ZBrush 2026 Python API (`zbrush.commands`): `set_next_filename(path)` then
`press("Tool:Import" / "Tool:Export")` — confirmed against the install's stubs at
`Documentation/python-api/stubs/zbrush/commands.pyi`. The `zbrush` import is guarded so the module
still imports in plain CPython (contract tests, `install_into_zbrush.py`); outside ZBrush the protocol
and path helpers work and the mesh op reports that ZBrush is required.

Loading in ZBrush: ZPlugin/ZScript → Python Scripting → Load → pick this file. Running it registers a
"ZPlugin:SPZ GO" subpalette with Import / Export / Answer SPZ buttons.
"""

from __future__ import annotations

import os
import time
from typing import Optional, Tuple

try:
    from . import spz_http  # installed as a package
except ImportError:  # pragma: no cover - direct-run / ZBrush flat import
    import spz_http  # type: ignore

HOST_ID = "zbrush"
BASE_URL_ENV = "SPZ_BASE_URL"
DEFAULT_BASE_URL = "http://127.0.0.1:5557"

# Namespaced under the shared SPZ exchange root so concurrent DCC handoffs never clobber each other.
EXCHANGE_ROOT_NAME = "StableProjectorzGO_exchange"
EXCHANGE_SUBDIR = HOST_ID

# Same literals SPZ writes (AddonUI_MGR.SpzGoSections.cs) — pinned by contract tests on both sides.
PULL_REQUEST_NAME = "spz_go_pull_request.json"
SPZ_PULL_BASENAME = "from_spz"          # SPZ → ZBrush (SPZ writes from_spz.fbx + .spz_go_ready stamp)
READY_STAMP_SUFFIX = ".spz_go_ready"
ZBRUSH_PUSH_BASENAME = "from_zbrush"    # ZBrush → SPZ


def base_url() -> str:
    return os.environ.get(BASE_URL_ENV) or DEFAULT_BASE_URL


def resolve_exchange_dir(url: Optional[str] = None) -> Tuple[Optional[str], str]:
    """Resolve <data_dir>/StableProjectorzGO_exchange/zbrush from SPZ project info."""
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


def push_mesh_path(exdir: str, ext: str = ".obj") -> str:
    return os.path.join(exdir, ZBRUSH_PUSH_BASENAME + ext)


# --- ZBrush mesh ops (real ZBrush 2026 Python API; guarded so plain CPython still imports) -----

def _zbc():
    """Return zbrush.commands, or None outside ZBrush."""
    try:
        import zbrush.commands as zbc  # type: ignore
        return zbc
    except Exception:
        return None


def _zbrush_available() -> bool:
    return _zbc() is not None


def _zbrush_export_active_tool(out_path: str) -> Tuple[bool, str]:
    """Export the active Tool/SubTool to out_path via Tool:Export with the filename pre-set."""
    zbc = _zbc()
    if zbc is None:
        return False, "ZBrush Python API unavailable — run inside ZBrush (or use GoZ manually)."
    try:
        # set_next_filename presets the path for the next Save/Load action; press Tool:Export fires it
        # without a dialog. Extension (.obj) selects the OBJ exporter that SPZ (Assimp) reads back.
        zbc.set_next_filename(out_path)
        zbc.press("Tool:Export")
        if os.path.isfile(out_path) and os.path.getsize(out_path) > 0:
            return True, out_path
        return False, "Tool:Export produced no file (no active Tool, or export template mismatch)."
    except Exception as e:
        return False, f"ZBrush export failed: {e}"


def _zbrush_import_mesh(in_path: str) -> Tuple[bool, str]:
    """Import in_path into the active Tool via Tool:Import with the filename pre-set."""
    zbc = _zbc()
    if zbc is None:
        return False, f"ZBrush Python API unavailable — import {in_path} via Tool:Import manually."
    try:
        zbc.set_next_filename(in_path)
        zbc.press("Tool:Import")
        return True, in_path
    except Exception as e:
        return False, f"ZBrush import failed: {e}"


# --- Public actions ---------------------------------------------------------------------------

def spz_export() -> Tuple[bool, str]:
    """ZBrush → SPZ: write the active Tool and ask SPZ to import it."""
    url = base_url()
    exdir, err = resolve_exchange_dir(url)
    if exdir is None:
        return False, err
    out = push_mesh_path(exdir, ".obj")
    ok, msg = _zbrush_export_active_tool(out)
    if not ok:
        return False, msg
    try:
        r = spz_http.post_import_3d_model(url, out)
    except spz_http.SpzHttpError as e:
        return False, str(e)
    if isinstance(r, dict) and r.get("success") is True:
        return True, f"Export → SPZ: {out}"
    return False, f"File written; SPZ import failed: {r!r}"


def spz_import() -> Tuple[bool, str]:
    """SPZ → ZBrush: ask SPZ to export its model to the exchange, then load it into ZBrush."""
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
    # HTTP already waited for mesh + textures; import when the file is present.
    for _ in range(60):
        if os.path.isfile(fbx) and os.path.getsize(fbx) > 32:
            break
        time.sleep(0.2)
    if not (os.path.isfile(fbx) and os.path.getsize(fbx) > 32):
        return False, f"SPZ reported OK but {fbx} not written."
    return _zbrush_import_mesh(fbx)


def spz_poll_pull_request() -> Tuple[bool, str]:
    """If SPZ pressed Import for ZBrush, consume the marker and push the active Tool once.

    Bind this to a ZScript button / hotkey the user taps after pressing Import in SPZ. Delete-then-push
    matches the Blender bridge so a repeat request writes a fresh marker.
    """
    exdir, err = resolve_exchange_dir()
    if exdir is None:
        return False, err
    req = pull_request_path(exdir)
    if not os.path.isfile(req):
        return False, "No pending SPZ import request for ZBrush."
    try:
        os.remove(req)
    except OSError as e:
        return False, f"Could not consume pull request: {e}"
    return spz_export()


# --- Palette registration (runs when ZBrush loads this file via ZScript → Load) ---------------

MENU_NAME = "ZPlugin:SPZ GO"


def _report(zbc, ok: bool, msg: str) -> None:
    try:
        zbc.set_notebar_text(("SPZ GO: " if ok else "SPZ GO (failed): ") + msg)
    except Exception:
        print("SPZ GO:", msg)


def _btn_import(item_path=None) -> None:
    zbc = _zbc()
    ok, msg = spz_import()
    if zbc is not None:
        _report(zbc, ok, msg)


def _btn_export(item_path=None) -> None:
    zbc = _zbc()
    ok, msg = spz_export()
    if zbc is not None:
        _report(zbc, ok, msg)


def _btn_answer(item_path=None) -> None:
    zbc = _zbc()
    ok, msg = spz_poll_pull_request()
    if zbc is not None:
        _report(zbc, ok, msg)


def create_palette() -> bool:
    """Register the SPZ GO subpalette + buttons. No-op (returns False) outside ZBrush."""
    zbc = _zbc()
    if zbc is None:
        return False
    zbc.add_subpalette(MENU_NAME, 0)
    zbc.add_button(f"{MENU_NAME}:Import from SPZ", "Pull the current StableProjectorz model", _btn_import)
    zbc.add_button(f"{MENU_NAME}:Export to SPZ", "Push the active Tool/SubTool to StableProjectorz", _btn_export)
    zbc.add_button(f"{MENU_NAME}:Answer SPZ request", "Run after pressing Import for ZBrush in SPZ", _btn_answer)
    return True


# ZBrush's run_path executes this file top-level; build the palette then. Guarded, so importing the
# module in plain CPython (tests / installer) does nothing.
if _zbrush_available():
    try:
        create_palette()
    except Exception as _e:  # never let palette build abort the load
        print("SPZ GO: palette registration failed:", _e)
