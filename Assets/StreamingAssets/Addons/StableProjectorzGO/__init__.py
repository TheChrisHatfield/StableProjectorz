"""
SPZ GO (in-app): exchange FBX/UV with Blender, headless import/export, optional Blender.exe path.
Same HTTP API as the external Blender add-on: import mesh path, export mesh path.
"""

import os
import re
import glob
import sys
import traceback

addon_system_dir = os.path.join(os.path.dirname(__file__), "..", "..", "AddonSystem")
if os.path.exists(addon_system_dir):
    sys.path.insert(0, addon_system_dir)

try:
    import spz  # type: ignore
    _SPZ_OK = True
except ImportError:
    _SPZ_OK = False
    spz = None  # type: ignore

# Must match the add-on folder name (Add-on Manager id).
ADDON_ID = "StableProjectorzGO"
EXCHANGE_DIRNAME = "StableProjectorzGO_exchange"
DEFAULT_EXCHANGE_IMPORT = "from_blender.fbx"  # Blender → disk → SPZ import
DEFAULT_EXCHANGE_EXPORT = "from_spz.fbx"  # SPZ → disk (Blender can import)

_panel = None
_eid_blender = None
_eid_import = None
_eid_export = None


def find_blender_executable():
    """
    Best-effort search for blender.exe (Windows: Program Files, Local AppData, Steam).
    On other OS returns empty (user can fill the field). Linux/mac: extend as needed.
    """
    if os.name != "nt":
        w = os.environ.get("PATH", "")
        for p in w.split(os.pathsep):
            if not p:
                continue
            cand = os.path.join(p, "blender")
            if os.path.isfile(cand) and os.access(cand, os.X_OK):
                return cand
        return ""

    candidates = []
    for env_key in ("ProgramFiles", "ProgramFiles(x86)"):
        root = os.environ.get(env_key, "")
        if not root:
            continue
        pat = os.path.join(root, "Blender Foundation", "Blender *", "blender.exe")
        try:
            candidates.extend(glob.glob(pat))
        except (OSError, TypeError):
            pass

    local_progs = os.path.join(os.environ.get("LOCALAPPDATA", ""), "Programs")
    if local_progs and os.path.isdir(local_progs):
        try:
            for sub in os.listdir(local_progs):
                if "Blender" in sub:
                    ex = os.path.join(local_progs, sub, "blender.exe")
                    if os.path.isfile(ex):
                        candidates.append(ex)
        except (OSError, NotADirectoryError, TypeError):
            pass

    steam = os.path.join(
        os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)"),
        "Steam", "steamapps", "common",
    )
    if os.path.isdir(steam):
        try:
            for sub in os.listdir(steam):
                if sub.startswith("Blender "):
                    ex = os.path.join(steam, sub, "blender.exe")
                    if os.path.isfile(ex):
                        candidates.append(ex)
        except (OSError, NotADirectoryError):
            pass

    if not candidates:
        return ""

    def _ver_key(path: str) -> tuple:
        m = re.search(r"Blender (\d+)\.(\d+)", path, re.I)
        if m:
            return (int(m.group(1)), int(m.group(2)))
        return (0, 0)

    return max(set(candidates), key=_ver_key)


def default_import_mesh_path():
    """Mesh file Blender should have written, for SPZ to import (absolute path if project saved)."""
    if not _SPZ_OK:
        return ""
    try:
        api = spz.get_api()
        d = api.project.get_data_dir()
        if d:
            return os.path.join(d, EXCHANGE_DIRNAME, DEFAULT_EXCHANGE_IMPORT)
    except Exception:
        pass
    return ""


def default_export_mesh_path():
    """Path where SPZ writes on Export (3D+textures) headless; Blender can import the same file."""
    if not _SPZ_OK:
        return ""
    try:
        api = spz.get_api()
        d = api.project.get_data_dir()
        if d:
            return os.path.join(d, EXCHANGE_DIRNAME, DEFAULT_EXCHANGE_EXPORT)
    except Exception:
        pass
    return ""


def do_import_from_path():
    global _panel, _eid_import
    if not _SPZ_OK:
        print(ADDON_ID + ": spz not available")
        return
    if _panel is None or _eid_import is None:
        print(ADDON_ID + ": panel not ready")
        return
    path = _panel.get_value(_eid_import)
    if not path or not str(path).strip():
        print(ADDON_ID + ": set Import mesh path first (or use Autofill)")
        return
    path = os.path.normpath(str(path).strip().strip('"'))
    if not path:
        return
    print(f"{ADDON_ID} import start:")
    print("  raw path:", repr(_panel.get_value(_eid_import)))
    print("  normalized path:", path)
    print("  exists:", os.path.isfile(path))
    if not os.path.isfile(path):
        print(ADDON_ID + ": import aborted — file not found:", path)
        try:
            spz.get_api().ui_chrome.show_status_text("Import: file not found", 4.0)
        except Exception as ex:
            print(ADDON_ID + " show_status_text:", ex)
        return
    try:
        print("  size_bytes:", os.path.getsize(path))
    except OSError as e:
        print("  size_bytes: <error>", e)
    api = spz.get_api()
    try:
        payload = {"filepath": str(path)}
        print("  rpc:", "spz.cmd.import_3d_model", payload)
        resp = api._client._send_request("spz.cmd.import_3d_model", payload)
        ok = bool(resp.get("success", False)) if isinstance(resp, dict) else False
        print(ADDON_ID + " import response:", repr(resp))
    except Exception as e:
        ok = False
        print(ADDON_ID + " import exception:", e)
        traceback.print_exc()
    try:
        api.ui_chrome.show_status_text("Import OK" if ok else "Import failed (check console)", 4.0)
    except Exception as ex:
        print(ADDON_ID + " show_status_text:", ex)


def do_export_to_path():
    global _panel, _eid_export
    if not _SPZ_OK:
        print(ADDON_ID + ": spz not available")
        return
    if _panel is None or _eid_export is None:
        print(ADDON_ID + ": panel not ready")
        return
    path = _panel.get_value(_eid_export)
    if not path or not str(path).strip():
        print(ADDON_ID + ": set Export mesh path first (or use Autofill)")
        return
    path = os.path.normpath(str(path).strip().strip('"'))
    if not path:
        return
    print(f"{ADDON_ID} export start:")
    print("  raw path:", repr(_panel.get_value(_eid_export)))
    print("  normalized path:", path)
    print("  dir exists before:", os.path.isdir(os.path.dirname(path) if os.path.dirname(path) else "."))
    api = spz.get_api()
    try:
        payload = {"mesh_filepath": str(path)}
        print("  rpc:", "spz.cmd.export_3d_with_textures_to_path", payload)
        resp = api._client._send_request("spz.cmd.export_3d_with_textures_to_path", payload)
        ok = bool(resp.get("success", False)) if isinstance(resp, dict) else False
        print(ADDON_ID + " export response:", repr(resp))
    except Exception as e:
        ok = False
        print(ADDON_ID + " export exception:", e)
        traceback.print_exc()
    try:
        print("  output exists after:", os.path.isfile(path))
        if os.path.isfile(path):
            print("  output size_bytes:", os.path.getsize(path))
    except OSError as e:
        print("  output stat error:", e)
    try:
        api.ui_chrome.show_status_text("Export OK" if ok else "Export failed (check console)", 5.0)
    except Exception as ex:
        print(ADDON_ID + " show_status_text:", ex)


def do_refresh_blender_path():
    global _panel, _eid_blender
    if _panel is None or _eid_blender is None:
        return
    p = find_blender_executable() or ""
    if _panel.set_value(_eid_blender, p):
        pass
    print(ADDON_ID + " Blender path:", p or "(not found — set manually)")


def do_autofill_mesh_paths():
    global _panel, _eid_import, _eid_export
    if not _SPZ_OK:
        return
    if _panel is None or _eid_import is None or _eid_export is None:
        return
    ip = default_import_mesh_path()
    ep = default_export_mesh_path()
    for eid, val in ((_eid_import, ip), (_eid_export, ep)):
        if val:
            if not _panel.set_value(eid, val):
                print(ADDON_ID + " could not set", eid)
    if not (ip and ep):
        print(ADDON_ID + ": save a project in SPZ to fill exchange paths, or type paths.")


def do_show_data_dir():
    if not _SPZ_OK:
        return
    api = spz.get_api()
    d = api.project.get_data_dir()
    if d:
        print(ADDON_ID + " data_dir:", d)
    else:
        print(ADDON_ID + ": no project data_dir — save a project in StableProjectorz first.")


def do_export_interactive():
    if not _SPZ_OK:
        return
    api = spz.get_api()
    ok = api.export.export_3d_with_textures()
    print(ADDON_ID + " export (interactive):", ok)


def register():
    global _panel, _eid_blender, _eid_import, _eid_export
    if not _SPZ_OK:
        print(ADDON_ID + ": cannot load — spz (AddonSystem) not importable")
        return
    api = spz.get_api()
    _panel = api.ui.create_panel(ADDON_ID, "SPZ GO")
    if not _panel:
        raise RuntimeError(
            ADDON_ID + ": create_panel failed — refusing successful load so Unity tears down the ribbon shell"
        )

    b_default = find_blender_executable() or ""
    i_default = default_import_mesh_path() or ""
    o_default = default_export_mesh_path() or ""

    _eid_blender = _panel.add_text_input("Blender.exe path (auto + editable)", b_default)
    _eid_import = _panel.add_text_input("Import: mesh file from Blender → SPZ", i_default)
    _eid_export = _panel.add_text_input("Export: mesh file from SPZ → disk", o_default)

    _panel.add_button("Refresh Blender path", "do_refresh_blender_path")
    _panel.add_button("Autofill import/export (needs saved project)", "do_autofill_mesh_paths")
    _panel.add_button("Import", "do_import_from_path")
    _panel.add_button("Export", "do_export_to_path")
    _panel.add_button("Export (file dialogs)…", "do_export_interactive")
    _panel.add_button("Print data_dir to log", "do_show_data_dir")
    print(ADDON_ID + " registered")


def unregister():
    global _panel, _eid_blender, _eid_import, _eid_export
    _panel = None
    _eid_blender = None
    _eid_import = None
    _eid_export = None
