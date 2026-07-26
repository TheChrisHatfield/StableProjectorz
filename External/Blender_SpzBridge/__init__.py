# SPDX-License-Identifier: MIT
# SPZ GO — Blender side: Import/Export with StableProjectorz over HTTP, shared exchange folder.
# Requires the game: TCP 5555 + HTTP 5557 (see StreamingAssets/AddonSystem).
#
# Headless: POST /api/v1/meshes/import, POST /api/v1/export/3d_to_path
# The in-app add-on: Assets/StreamingAssets/Addons/StableProjectorzGO/

bl_info = {
    "name": "SPZ GO (HTTP)",
    "author": "StableProjectorz / community",
    "version": (0, 2, 0),
    "blender": (4, 0, 0),
    "location": "3D Viewport: N (toggle sidebar) → top tab 'SPZ GO' (not auto-open; scroll tab row if needed)",
    "description": "Link with StableProjectorz: pull/push 3D via REST; shared exchange folder; FBX and UV (SVG) helpers",
    "category": "Import-Export",
}

import os
from pathlib import Path
from typing import Optional, Tuple

# Default names for exchange; Unity autofill uses the same (StableProjectorzGO/__init__.py).
EXCHANGE_FBX_FROM_BLENDER = "from_blender.fbx"

import bpy
from bpy.props import StringProperty, BoolProperty
from bpy.types import Operator, Panel, AddonPreferences

from . import spz_http

# blender_manifest "id" — preferences bl_idname and official extension install key
ADDON_ID_MANIFEST = "spz_blender_bridge"

def _package_root_name() -> str:
    """
    Folder name of this add-on = modules key in bpy.context.preferences.addons.
    Do not use the first segment of __name__ — extension installs use bl_ext.*.pkg,
    and split(...)[0] becomes 'bl_ext' (wrong key → 'bl_ext' not found errors).
    """
    try:
        d = os.path.dirname(os.path.realpath(__file__))
        n = os.path.basename(d)
        if n in ("", ".", os.path.sep) or n == os.pathsep:
            return ADDON_ID_MANIFEST
        return n
    except Exception:
        return ADDON_ID_MANIFEST


def _addon_module() -> str:
    """Name Blender uses for this add-on in preferences (same as <package> folder on disk, or manifest id)."""
    return _package_root_name()


# --- Uninstall (defined before StableProjectorzGOPreferences.draw) ---

class SPZ_OT_addon_uninstall(Operator):
    bl_idname = "spz.addon_uninstall"
    bl_label = "Remove SPZ GO…"
    bl_description = (
        "Uninstall this add-on from Blender. Same as removing it in Preferences → Add-ons. "
        "Reinstall from the project zip if needed."
    )
    bl_options = {"REGISTER"}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        remove_keys = []
        for k in (_package_root_name(), ADDON_ID_MANIFEST, "Blender_SpzBridge"):
            if not k or k in remove_keys:
                continue
            if k == "bl_ext":
                continue
            remove_keys.append(k)

        def _remove() -> None:
            last = None
            for mod in remove_keys:
                try:
                    bpy.ops.preferences.addon_remove(module=mod)
                    return
                except Exception as e:
                    last = e
            if last is not None:
                print("SPZ GO: addon_remove failed for", remove_keys, "—", last)

        bpy.app.timers.register(_remove, first_interval=0.1)
        return {"FINISHED"}


# --- Preferences -----------------------------------------------------------------

class StableProjectorzGOPreferences(AddonPreferences):
    # Must be the registered module name: same as prefs.addons key (dev folder, zip, or bl_ext dotted id).
    bl_idname = __name__

    base_url: StringProperty(
        name="Base URL",
        default="http://127.0.0.1:5557",
        description="StableProjectorz HTTP REST (see Assets/StreamingAssets/AddonSystem/http_server.py)",
    )
    exchange_subdir: StringProperty(
        name="Exchange subfolder",
        default="StableProjectorzGO_exchange",
        description="Under project data_dir; use the same in the in-app SPZ GO add-on.",
    )
    uv_svg_name: StringProperty(
        name="UV layout name",
        default="uv_layout",
    )
    spz_pull_basename: StringProperty(
        name="SPZ export base name",
        default="from_spz",
        description="Headless pull from SPZ: mesh+textures written as <name>.fbx under the exchange folder",
    )

    def draw(self, context):
        info = self.layout.box()
        info.label(
            text="N-panel: open 3D View, press N (or View → Sidebar), then the tab 'SPZ GO' (scroll the tab bar if you use many add-ons).",
            icon="INFO",
        )
        self.layout.separator()
        self.layout.prop(self, "base_url")
        self.layout.prop(self, "exchange_subdir")
        self.layout.prop(self, "uv_svg_name")
        self.layout.prop(self, "spz_pull_basename")
        self.layout.separator()
        box = self.layout.box()
        box.label(
            text="Uninstall: removes SPZ GO (reinstall the zip to add again).",
            icon="ERROR",
        )
        box.operator("spz.addon_uninstall", icon="TRASH")


def prefs():
    addons = bpy.context.preferences.addons
    keys = [
        __name__,
        ADDON_ID_MANIFEST,
        _package_root_name(),
        "Blender_SpzBridge",  # repo / legacy
        __name__.rsplit(".", 1)[-1] if __name__ else None,
    ]
    seen = set()
    for k in keys:
        if not k or k in seen:
            continue
        if k == "bl_ext":
            continue
        seen.add(k)
        try:
            ad = addons[k]
            p = ad.preferences
        except (KeyError, TypeError):
            continue
        except Exception:
            # Wrong add-on struct / bad key — try next
            continue
        if p is not None and isinstance(p, StableProjectorzGOPreferences):
            return p
    # Key-based lookup can fail when the real module name differs (Extensions, zips, nested bl_ext).
    # Scan all enabled add-ons and match this module's registered AddonPreferences class.
    try:
        for addon in addons:
            try:
                p = addon.preferences
            except (AttributeError, TypeError, ReferenceError):
                continue
            if p is not None and isinstance(p, StableProjectorzGOPreferences):
                return p
    except (TypeError, RuntimeError) as e:
        print("SPZ GO: prefs() addon scan failed:", e)
    raise KeyError(
        "SPZ GO: preferences not found — enable the add-on, or re-enable after an update. "
        "The add-on folder on disk (module name) must be stable; official extension id is '"
        + ADDON_ID_MANIFEST
        + "'."
    )


def _ensure_dir(path: str) -> bool:
    try:
        os.makedirs(path, exist_ok=True)
        return True
    except OSError:
        return False


def _exchange_fbx_path(context) -> Tuple[Optional[str], str]:
    """Return (abs_path to expected FBX, err_msg) — err_msg set only for failure; path may be None."""
    p = prefs()
    try:
        info = spz_http.get_project_info(p.base_url)
    except spz_http.SpzHttpError as e:
        return None, str(e)
    if not info.get("data_dir_available") or not info.get("data_dir"):
        return None, "No project data_dir in SPZ — save a project in StableProjectorz first."
    exdir = os.path.join(info["data_dir"], p.exchange_subdir)
    fbx = str(Path(exdir) / EXCHANGE_FBX_FROM_BLENDER)
    return fbx, ""


def _spz_headless_out_fbx() -> Tuple[Optional[str], str]:
    p = prefs()
    try:
        info = spz_http.get_project_info(p.base_url)
    except spz_http.SpzHttpError as e:
        return None, str(e)
    if not info.get("data_dir_available") or not info.get("data_dir"):
        return None, "No project data_dir in SPZ — save a project in StableProjectorz first."
    exdir = os.path.join(info["data_dir"], p.exchange_subdir)
    base = (bpy.path.clean_name(p.spz_pull_basename) or "from_spz").strip()
    fbx = str(Path(exdir) / f"{base}.fbx")
    return fbx, ""


def _find_best_exchange_texture_for_fbx(fbx_path: str) -> Optional[str]:
    """Find best texture candidate written beside the exported FBX."""
    if not fbx_path:
        return None
    folder = os.path.dirname(fbx_path)
    if not folder or not os.path.isdir(folder):
        return None
    base = Path(fbx_path).stem.lower()
    exts = (".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tif", ".tiff", ".webp", ".exr")
    try:
        files = [f for f in os.listdir(folder) if os.path.splitext(f)[1].lower() in exts]
    except OSError:
        return None
    if not files:
        return None
    ranked = []
    for name in files:
        low = name.lower()
        score = 0
        if low.startswith(base):
            score += 100
        if base in low:
            score += 40
        if any(k in low for k in ("albedo", "basecolor", "base_color", "diffuse", "color")):
            score += 30
        if "normal" in low or "rough" in low or "metal" in low or "ao" in low:
            score -= 20
        ranked.append((score, name))
    ranked.sort(key=lambda x: (-x[0], x[1]))
    best = ranked[0][1]
    return str(Path(folder) / best)


def _ensure_image_to_principled_basecolor(obj, image_path: str):
    """Ensure obj material has UV -> Image Texture -> Principled Base Color wiring."""
    if obj is None or getattr(obj, "type", None) != "MESH":
        return
    if not image_path or not os.path.isfile(image_path):
        return
    mat = obj.active_material
    if mat is None:
        mat = bpy.data.materials.new(name=f"{obj.name}_SPZ")
        obj.active_material = mat
    mat.use_nodes = True
    nt = mat.node_tree
    if nt is None:
        return
    nodes = nt.nodes
    links = nt.links
    principled = None
    out = None
    for n in nodes:
        if n.type == "BSDF_PRINCIPLED" and principled is None:
            principled = n
        elif n.type == "OUTPUT_MATERIAL" and out is None:
            out = n
    if principled is None:
        principled = nodes.new("ShaderNodeBsdfPrincipled")
        principled.location = (0, 0)
    if out is None:
        out = nodes.new("ShaderNodeOutputMaterial")
        out.location = (280, 0)
    # Ensure BSDF -> Output is connected
    if not any(l.from_node == principled and l.to_node == out for l in links):
        try:
            links.new(principled.outputs["BSDF"], out.inputs["Surface"])
        except Exception:
            pass
    # Image texture node
    tex = None
    for n in nodes:
        if n.type == "TEX_IMAGE" and getattr(getattr(n, "image", None), "filepath", ""):
            if bpy.path.abspath(n.image.filepath) == bpy.path.abspath(image_path):
                tex = n
                break
    if tex is None:
        tex = nodes.new("ShaderNodeTexImage")
        tex.location = (-560, 0)
    try:
        img = bpy.data.images.load(image_path, check_existing=True)
        tex.image = img
    except Exception as e:
        print("SPZ GO: could not load image:", image_path, e)
        return
    # UV map node (explicitly uses active UV map for this mesh if present)
    uv = None
    for n in nodes:
        if n.type == "UVMAP":
            uv = n
            break
    if uv is None:
        uv = nodes.new("ShaderNodeUVMap")
        uv.location = (-800, 0)
    try:
        if obj.data and obj.data.uv_layers and obj.data.uv_layers.active:
            uv.uv_map = obj.data.uv_layers.active.name
    except Exception:
        pass
    try:
        if not any(l.from_node == uv and l.to_node == tex and l.to_socket.name == "Vector" for l in links):
            links.new(uv.outputs["UV"], tex.inputs["Vector"])
    except Exception:
        pass
    # Image color -> Principled base color
    try:
        base_in = principled.inputs.get("Base Color")
        if base_in is not None:
            for l in list(base_in.links):
                links.remove(l)
            links.new(tex.outputs["Color"], base_in)
    except Exception:
        pass


def _auto_apply_exchange_texture_after_import(fbx_path: str) -> bool:
    """Apply best exchange texture to selected/active meshes. Returns True if at least one mesh was wired."""
    tex = _find_best_exchange_texture_for_fbx(fbx_path)
    if not tex:
        print("SPZ GO: no exchange texture found to auto-assign for", fbx_path)
        return False
    targets = [o for o in bpy.context.selected_objects if o and o.type == "MESH"]
    if not targets and bpy.context.active_object and bpy.context.active_object.type == "MESH":
        targets = [bpy.context.active_object]
    if not targets:
        print("SPZ GO: no target mesh object selected after import; texture not auto-assigned.")
        return False
    for o in targets:
        _ensure_image_to_principled_basecolor(o, tex)
    print(f"SPZ GO: auto-assigned texture '{tex}' to {len(targets)} mesh object(s).")
    return True


def _export_fbx_for_spz(context, filepath: str) -> set:
    """
    If any objects are selected: export the *active* object only (or first in selection if active
    is not in the selection), then restore selection. If nothing is selected: `use_selection=False`
    (same as Blender's default File → Export → FBX, i.e. full scene to FBX).
    """
    if not context.selected_objects:
        return bpy.ops.export_scene.fbx(filepath=filepath, use_selection=False)
    act = context.view_layer.objects.active
    if act is not None and act.select_get():
        target = act
    else:
        target = context.selected_objects[0]
    old_sel = list(context.selected_objects)
    name_active = (
        context.view_layer.objects.active.name
        if context.view_layer.objects.active is not None
        else None
    )
    try:
        bpy.ops.object.select_all(action="DESELECT")
        target.select_set(True)
        context.view_layer.objects.active = target
        return bpy.ops.export_scene.fbx(filepath=filepath, use_selection=True)
    finally:
        try:
            bpy.ops.object.select_all(action="DESELECT")
            for o in old_sel:
                try:
                    o.select_set(True)
                except ReferenceError:
                    pass
            if name_active and name_active in context.view_layer.objects:
                context.view_layer.objects.active = bpy.data.objects[name_active]
        except Exception as e:
            print("SPZ GO: could not fully restore selection after export:", e)


# --- SPZ → Blender: wait for exported FBX, then import ---

_go_timer_state = None


def _go_import_timer():
    global _go_timer_state
    if not _go_timer_state:
        return None
    path, n = _go_timer_state
    if n > 100:
        _go_timer_state = None
        print("SPZ GO: timeout waiting for:", path)
        return None
    if os.path.isfile(path) and os.path.getsize(path) > 32:
        try:
            bpy.ops.import_scene.fbx(filepath=path)
            _auto_apply_exchange_texture_after_import(path)
        except Exception as e:
            print("SPZ GO import_scene.fbx:", e)
        _go_timer_state = None
        return None
    _go_timer_state = (path, n + 1)
    return 0.2


# --- Operators -------------------------------------------------------------------

class SPZ_OT_test_connection(Operator):
    bl_idname = "spz.test_connection"
    bl_label = "Test connection"
    bl_description = "GET /api/v1/project/info"

    def execute(self, context):
        p = prefs()
        try:
            info = spz_http.get_project_info(p.base_url)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        # Flatten a short status line
        self.report(
            {"INFO"},
            f"version={info.get('version')!r} data_dir={info.get('data_dir')!r}",
        )
        return {"FINISHED"}


class SPZ_OT_unity_request_export(Operator):
    bl_idname = "spz.unity_request_export_3d"
    bl_label = "Trigger SPZ export (3D+textures)"
    bl_description = (
        "POST /api/v1/export/3d_with_textures — may open file dialogs in Unity; "
        "use for the same action as the in-app exporter"
    )

    def execute(self, context):
        p = prefs()
        try:
            r = spz_http.post_export_3d_with_textures(p.base_url)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if ok:
            self.report({"INFO"}, "Request sent. Complete any save dialogs in StableProjectorz.")
        else:
            self.report({"WARNING"}, f"Response: {r!r}")
        return {"FINISHED"}


class SPZ_OT_unity_request_proj_tex(Operator):
    bl_idname = "spz.unity_request_projection_textures"
    bl_label = "Trigger SPZ export (projection textures)"
    bl_options = {"REGISTER"}

    is_dilate: BoolProperty(name="Dilate", default=True)

    def execute(self, context):
        p = prefs()
        try:
            r = spz_http.post_export_projection_textures(p.base_url, is_dilate=self.is_dilate)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        self.report({"INFO"}, f"Request sent: {r!r}")
        return {"FINISHED"}


class SPZ_OT_blender_export_selection_fbx(Operator):
    bl_idname = "spz.blender_export_fbx"
    bl_label = "Export to SPZ exchange (FBX)"
    bl_options = {"REGISTER", "UNDO"}
    bl_description = (
        "With a selection: writes the active object only. With no selection: full-scene export "
        "(use_selection=False) like default FBX, to the exchange from_blender.fbx."
    )

    def execute(self, context):
        p = prefs()
        try:
            info = spz_http.get_project_info(p.base_url)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        if not info.get("data_dir_available") or not info.get("data_dir"):
            self.report(
                {"ERROR"},
                "Project has no data_dir. Save a StableProjectorz project first, then retry.",
            )
            return {"CANCELLED"}
        exdir = os.path.join(info["data_dir"], p.exchange_subdir)
        if not _ensure_dir(exdir):
            self.report({"ERROR"}, f"Could not create: {exdir}")
            return {"CANCELLED"}
        out = str(Path(exdir) / EXCHANGE_FBX_FROM_BLENDER)
        if bpy.ops.export_scene.fbx.poll() is False:
            self.report(
                {"ERROR"},
                "SPZ GO: FBX export is not available in this context (3D Viewport object mode).",
            )
            return {"CANCELLED"}
        ret = _export_fbx_for_spz(context, out)
        if not (ret and ("FINISHED" in ret)):
            self.report({"ERROR"}, f"Blender FBX export did not finish: {ret!r}")
            return {"CANCELLED"}
        self.report({"INFO"}, f"Wrote: {out}")
        return {"FINISHED"}


class SPZ_OT_go_export(Operator):
    bl_idname = "spz.go_export"
    bl_label = "Export to StableProjectorz"
    bl_options = {"REGISTER", "UNDO"}
    bl_description = (
        "With a selection: write the active object to the exchange FBX. With no selection: full "
        "scene to FBX (use_selection=False) like default File → Export → FBX. Then headless import in SPZ."
    )

    def execute(self, context):
        p = prefs()
        try:
            info = spz_http.get_project_info(p.base_url)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        if not info.get("data_dir_available") or not info.get("data_dir"):
            self.report(
                {"ERROR"},
                "No project data_dir. Save a StableProjectorz project first.",
            )
            return {"CANCELLED"}
        exdir = os.path.join(info["data_dir"], p.exchange_subdir)
        if not _ensure_dir(exdir):
            self.report({"ERROR"}, f"Could not create: {exdir}")
            return {"CANCELLED"}
        out = str(Path(exdir) / EXCHANGE_FBX_FROM_BLENDER)
        if bpy.ops.export_scene.fbx.poll() is False:
            self.report(
                {"ERROR"},
                "SPZ GO: FBX export is not available in this context (3D Viewport object mode).",
            )
            return {"CANCELLED"}
        ret = _export_fbx_for_spz(context, out)
        if not (ret and ("FINISHED" in ret)):
            self.report({"ERROR"}, f"Blender FBX export did not finish: {ret!r}")
            return {"CANCELLED"}
        try:
            r = spz_http.post_import_3d_model(p.base_url, out)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if ok:
            self.report({"INFO"}, f"Export → SPZ: {out}")
        else:
            self.report({"WARNING"}, f"File written; SPZ import: {r!r}")
        return {"FINISHED"}


class SPZ_OT_go_import(Operator):
    bl_idname = "spz.go_import"
    bl_label = "Import from StableProjectorz"
    bl_options = {"REGISTER", "UNDO"}
    bl_description = (
        "Request headless export from StableProjectorz, then import the exchange FBX here (SPZ to Blender)."
    )

    def execute(self, context):
        global _go_timer_state
        p = prefs()
        fbx, err = _spz_headless_out_fbx()
        if fbx is None:
            self.report({"ERROR"}, err)
            return {"CANCELLED"}
        d = os.path.dirname(fbx)
        if d and not _ensure_dir(d):
            self.report({"ERROR"}, f"Could not create: {d}")
            return {"CANCELLED"}
        try:
            r = spz_http.post_export_3d_to_path(p.base_url, fbx)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if not ok:
            self.report({"WARNING"}, f"SPZ: {r!r}")
            return {"CANCELLED"}
        _go_timer_state = (fbx, 0)
        try:
            if hasattr(bpy.app.timers, "is_registered") and bpy.app.timers.is_registered(
                _go_import_timer
            ):
                pass
            else:
                bpy.app.timers.register(_go_import_timer, first_interval=0.25)
        except Exception:
            bpy.app.timers.register(_go_import_timer, first_interval=0.25)
        self.report({"INFO"}, f"Import ← SPZ: waiting for {fbx}")
        return {"FINISHED"}


class SPZ_OT_go_apply_maps_only(Operator):
    bl_idname = "spz.go_apply_maps_only"
    bl_label = "Apply SPZ maps only"
    bl_options = {"REGISTER", "UNDO"}
    bl_description = (
        "Request SPZ headless export to refresh exchange textures, then apply the texture map to "
        "currently selected mesh object(s) without importing/replacing FBX geometry."
    )

    def execute(self, context):
        p = prefs()
        fbx, err = _spz_headless_out_fbx()
        if fbx is None:
            self.report({"ERROR"}, err)
            return {"CANCELLED"}
        d = os.path.dirname(fbx)
        if d and not _ensure_dir(d):
            self.report({"ERROR"}, f"Could not create: {d}")
            return {"CANCELLED"}
        try:
            r = spz_http.post_export_3d_to_path(p.base_url, fbx)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if not ok:
            self.report({"WARNING"}, f"SPZ: {r!r}")
            return {"CANCELLED"}
        if not _auto_apply_exchange_texture_after_import(fbx):
            self.report(
                {"WARNING"},
                "SPZ export OK but no texture applied — select a mesh and ensure exchange maps exist beside the FBX.",
            )
            return {"CANCELLED"}
        self.report({"INFO"}, "SPZ maps applied to selected mesh(es).")
        return {"FINISHED"}


class SPZ_OT_export_uv_svg(Operator):
    bl_idname = "spz.export_uv_layout_svg"
    bl_label = "Export UV layout (SVG)"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        return obj and obj.type == "MESH"

    def execute(self, context):
        p = prefs()
        try:
            info = spz_http.get_project_info(p.base_url)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        if not info.get("data_dir_available") or not info.get("data_dir"):
            self.report(
                {"ERROR"},
                "Project has no data_dir. Save a StableProjectorz project first.",
            )
            return {"CANCELLED"}
        exdir = os.path.join(info["data_dir"], p.exchange_subdir)
        if not _ensure_dir(exdir):
            self.report({"ERROR"}, f"Could not create: {exdir}")
            return {"CANCELLED"}
        name = f"{bpy.path.clean_name(context.active_object.name)}_{p.uv_svg_name}.svg"
        out = str(Path(exdir) / name)
        w = 2048
        h = 2048
        obj = context.active_object
        prev = obj.mode
        try:
            if prev != "EDIT":
                bpy.ops.object.mode_set(mode="EDIT")
            try:
                bpy.ops.uv.export_layout(
                    filepath=out,
                    check_existing=True,
                    export_all=True,
                    export_tiles="NONE",
                    modified=True,
                    mode="SVG",
                    size=(w, h),
                    opacity=0.25,
                )
            except TypeError:
                bpy.ops.uv.export_layout(
                    filepath=out,
                    export_all=True,
                    modified=True,
                    mode="SVG",
                    size=(w, h),
                    opacity=0.25,
                )
        finally:
            if obj.mode != prev:
                bpy.ops.object.mode_set(mode=prev)
        self.report({"INFO"}, f"Wrote: {out}")
        return {"FINISHED"}


class SPZ_OT_request_spz_import_fbx(Operator):
    bl_idname = "spz.request_spz_import_exchange_fbx"
    bl_label = "Import exchange FBX in SPZ (headless)"
    bl_description = "POST /api/v1/meshes/import for the exchange from_blender.fbx (use Export, or after export to that file)."

    def execute(self, context):
        p = prefs()
        fbx, err = _exchange_fbx_path(context)
        if fbx is None:
            self.report({"ERROR"}, err)
            return {"CANCELLED"}
        if not os.path.isfile(fbx):
            self.report(
                {"ERROR"},
                f"File not found: {fbx}\nUse 'Export to exchange (FBX)' first (or share a drive both apps read).",
            )
            return {"CANCELLED"}
        try:
            r = spz_http.post_import_3d_model(p.base_url, fbx)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if ok:
            self.report({"INFO"}, "SPZ import request OK.")
        else:
            self.report({"WARNING"}, f"Response: {r!r}")
        return {"FINISHED"}


class SPZ_OT_request_spz_export_to_path(Operator):
    bl_idname = "spz.request_spz_export_3d_to_path"
    bl_label = "Pull: SPZ → disk (3D+textures, headless)"
    bl_description = "POST /api/v1/export/3d_to_path — writes under exchange folder; no Unity save dialog"

    def execute(self, context):
        p = prefs()
        fbx, err = _spz_headless_out_fbx()
        if fbx is None:
            self.report({"ERROR"}, err)
            return {"CANCELLED"}
        d = os.path.dirname(fbx)
        if d and not _ensure_dir(d):
            self.report({"ERROR"}, f"Could not create: {d}")
            return {"CANCELLED"}
        try:
            r = spz_http.post_export_3d_to_path(p.base_url, fbx)
        except spz_http.SpzHttpError as e:
            self.report({"ERROR"}, str(e))
            return {"CANCELLED"}
        ok = (r.get("success") is True) if isinstance(r, dict) else False
        if ok:
            self.report({"INFO"}, f"Request OK — SPZ writes: {fbx} (+ textures beside base name)")
        else:
            self.report({"WARNING"}, f"Response: {r!r}")
        return {"FINISHED"}


# --- Panel -----------------------------------------------------------------------

class SPZ_PT_main(Panel):
    bl_label = "SPZ GO"
    bl_idname = "SPZ_GO_PT_main"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "SPZ GO"

    @classmethod
    def poll(cls, context):
        # context.area is sometimes None during redraw; space_data is reliable in the 3D Viewport.
        if not context:
            return False
        try:
            sd = context.space_data
            if sd is not None and getattr(sd, "type", None) == "VIEW_3D":
                return True
        except (AttributeError, TypeError, ReferenceError):
            pass
        a = getattr(context, "area", None)
        return a is not None and a.type == "VIEW_3D"

    def draw(self, context):
        l = self.layout
        try:
            p = prefs()
        except Exception as e:
            l.label(text="Could not read add-on preferences", icon="ERROR")
            l.label(text=str(e))
            return
        l.label(text="StableProjectorz link (HTTP :5557):", icon="URL")
        l.label(text="• Import: SPZ writes FBX+textures, opens here")
        l.label(
            text="• Export: active if selected, else full scene (like default FBX) → from_blender.fbx"
        )
        l.prop(p, "base_url", text="Base URL")
        l.prop(p, "exchange_subdir", text="Exchange subfolder")
        l.separator()
        b = l.box()
        b.label(text="Main", icon="LINKED")
        b.operator(
            SPZ_OT_go_import.bl_idname,
            text=SPZ_OT_go_import.bl_label,
            icon="IMPORT",
        )
        b.operator(
            SPZ_OT_go_apply_maps_only.bl_idname,
            text=SPZ_OT_go_apply_maps_only.bl_label,
            icon="TEXTURE",
        )
        b.operator(SPZ_OT_go_export.bl_idname, text=SPZ_OT_go_export.bl_label, icon="EXPORT")
        l.separator()
        l.label(text="Options / test")
        col = l.column(align=True)
        col.operator(SPZ_OT_test_connection.bl_idname, text="Test connection", icon="CONSOLE")
        col.operator(SPZ_OT_unity_request_export.bl_idname, text="SPZ save dialogs export", icon="FILEBROWSER")
        col2 = l.column(align=True)
        col2.operator(SPZ_OT_unity_request_proj_tex.bl_idname, icon="TEXTURE")
        l.label(text="Split headless (advanced)")
        a = l.column(align=True)
        a.operator(SPZ_OT_request_spz_import_fbx.bl_idname, icon="LINKED")
        a.operator(SPZ_OT_request_spz_export_to_path.bl_idname, icon="FILE_TICK")
        a.operator(SPZ_OT_blender_export_selection_fbx.bl_idname, icon="MESH_DATA")
        a.operator(SPZ_OT_export_uv_svg.bl_idname, icon="UV")
        l.label(text="data_dir and exchange subfolder must match the in-app GO add-on.")
        l.separator()
        u = l.box()
        u.label(text="Uninstall (confirms)", icon="TRASH")
        u.operator("spz.addon_uninstall", text="Remove SPZ GO", icon="TRASH")


# --- register -------------------------------------------------------------------

classes = (
    StableProjectorzGOPreferences,
    SPZ_OT_addon_uninstall,
    SPZ_OT_test_connection,
    SPZ_OT_unity_request_export,
    SPZ_OT_unity_request_proj_tex,
    SPZ_OT_blender_export_selection_fbx,
    SPZ_OT_go_export,
    SPZ_OT_go_import,
    SPZ_OT_go_apply_maps_only,
    SPZ_OT_request_spz_import_fbx,
    SPZ_OT_request_spz_export_to_path,
    SPZ_OT_export_uv_svg,
    SPZ_PT_main,
)


def register():
    for c in classes:
        try:
            bpy.utils.register_class(c)
        except Exception as e:
            print("SPZ GO: register_class failed for", getattr(c, "__name__", str(c)), "—", e)
    print(
        "SPZ GO: add-on on. 3D View: press N (or View → Sidebar) → top tab 'SPZ GO'."
    )


def unregister():
    for c in reversed(classes):
        try:
            bpy.utils.unregister_class(c)
        except Exception as e:
            print("SPZ GO: unregister_class failed for", getattr(c, "__name__", str(c)), "—", e)
