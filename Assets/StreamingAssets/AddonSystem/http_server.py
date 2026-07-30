#!/usr/bin/env python3
"""
FastAPI HTTP Server for StableProjectorz
Provides REST API endpoints that forward to Unity via JSON-RPC.

Scene/control: ``/api/v1/cameras``, ``/api/v1/view-cameras/*`` (multi-slot view camera enable/current/projection),
``projection/*``, ``meshes`` (transforms, selection, batch, import), ``scene``, ``sd``, ``project``, ``gen3d/*``,
``export/*`` (including non-interactive ``export/3d_to_path``) → ``spz.cmd.*``.

Paint/brush: ``/api/v1/paint/*`` → ``spz.cmd.get_brush_settings``, ``get_paint_layers``, ``set_brush_*``, ``set_active_paint_layer``.

SD / Forge-style: ``/api/v1/sd/workflow/*``, ``/api/v1/sd/generation/*``, ``/api/v1/sd/controlnet/*``,
``/api/v1/sd/skybox/*`` → workflow mode, denoise/blur/toggles, ControlNet units, skybox (same as ``spz.cmd.*``).

Add-on panel UI (same as Python ``api.ui`` over TCP): ``/api/v1/ui/*`` → ``spz.ui.*``
(create_panel, buttons, sliders, inputs, dropdowns, get/set widget values; ``spz.ui.attach_viewport_fullview_toggle`` is JSON-RPC-only for the SD ribbon full-view control).

Meta / discovery: ``GET /api/v1/meta`` → ``spz.cmd.get_api_capabilities`` (method list + RPC version);
``GET /api/v1/context`` → ``spz.cmd.get_addon_context`` (scene + SD + brush snapshot).

Editor chrome: ``GET/POST /api/v1/editor/layout`` → ``spz.cmd.get_editor_layout`` / ``set_editor_layout``
(left/right column visibility, ``viewport_focus`` / ``fullscreen_center`` mode for center viewport width).

Display / OS fullscreen: ``GET/POST /api/v1/display/mode`` → ``spz.cmd.get_display_mode`` / ``set_display_mode``
(windowed, ``exclusive_fullscreen``, ``borderless_fullscreen``; optional width/height/refresh_rate_hz).

UI chrome (ribbon + cursor): ``GET/POST /api/v1/chrome/ribbon/tabs|tab``, ``GET/POST /api/v1/chrome/cursor``
→ ``spz.cmd.get_ribbon_tabs``, ``set_ribbon_tab``, ``get_cursor_state``, ``set_cursor_state``.

UI chrome (scale / targets / status / EventSystem): ``/api/v1/chrome/ui-scale``, ``/api/v1/chrome/ui-targets``,
``/api/v1/chrome/status-text``, ``/api/v1/chrome/event-system`` → matching ``spz.cmd.*``.

Blocking work (Unity JSON-RPC over TCP, connection-ready probe, import/exec in
load_addon / invoke_callback) runs in asyncio.to_thread so the event loop stays free.
"""

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, field_validator
from typing import Optional, List, Dict, Any
import asyncio
import importlib.util
import uvicorn
from pathlib import Path

# Import spz for Unity communication
try:
    import spz
except ImportError:
    print("Error: Could not import spz module. Make sure spz.py is in the AddonSystem directory.")
    raise

# Global API instance (will be set by addon_server.py)
_api = None

def set_api_instance(api_instance):
    """Set the global API instance"""
    global _api
    _api = api_instance

# Callback to load an addon by id (set by addon_server.py)
_load_addon_callback = None

def set_load_addon_callback(callback):
    """Set the callback used by POST /load_addon. Signature: (addon_id: str) -> bool"""
    global _load_addon_callback
    _load_addon_callback = callback

# Callback to unload an addon by id (set by addon_server.py)
_unload_addon_callback = None

def set_unload_addon_callback(callback):
    """Set the callback used by POST /unload_addon. Signature: (addon_id: str) -> bool"""
    global _unload_addon_callback
    _unload_addon_callback = callback

# Callback: True when Python has connected to Unity socket (set by addon_server.py)
_connection_ready_callback = None

def set_connection_ready_callback(callback):
    """Set the callback for GET /ready. Signature: () -> bool. Unity should wait for ready before POST /load_addon."""
    global _connection_ready_callback
    _connection_ready_callback = callback

# Callback to invoke an addon function by name (set by addon_server.py)
_invoke_callback = None

def set_invoke_callback(callback):
    """Set the callback for POST /invoke_callback. Signature: (addon_id: str, callback_name: str) -> bool"""
    global _invoke_callback
    _invoke_callback = callback

# Callback for Unity widget value changes (set by addon_server.py)
_notify_value_change_callback = None

def set_notify_value_change_callback(callback):
    """Set the callback for POST /notify_value_change.
    Signature: (addon_id: str, element_id: str, element_type: str, value: Any) -> bool
    """
    global _notify_value_change_callback
    _notify_value_change_callback = callback

# FastAPI app
app = FastAPI(
    title="StableProjectorz API",
    description="REST API for controlling StableProjectorz",
    version="1.0.0"
)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Can be configured
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


def _mount_optional_gpu_flow_routes() -> None:
    """Mount ``GpuFlowSPZ`` REST helpers if ``StreamingAssets/Addons/GpuFlowSPZ/http_routes.py`` exists."""
    try:
        addon_dir = Path(__file__).resolve().parent.parent / "Addons" / "GpuFlowSPZ"
        route_path = addon_dir / "http_routes.py"
        if not route_path.is_file():
            return
        spec = importlib.util.spec_from_file_location("spz_gpu_flow_http", route_path)
        if spec is None or spec.loader is None:
            return
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        reg = getattr(mod, "register_routes", None)
        if callable(reg) and reg(app):
            print("[HTTP Server] GPU Flow: GET/POST /api/v1/gpu-flow/status | /api/v1/gpu-flow/pace")
    except Exception as e:
        print(f"[HTTP Server] GPU Flow routes not mounted: {e}")


_mount_optional_gpu_flow_routes()

# Pydantic models for request bodies
class Position(BaseModel):
    x: float
    y: float
    z: float

class Rotation(BaseModel):
    x: float
    y: float
    z: float
    w: float

class Prompt(BaseModel):
    positive: Optional[str] = None
    negative: Optional[str] = None

class EditorLayoutBody(BaseModel):
    """Optional ``mode`` (``default`` | ``viewport_focus`` | ``fullscreen_center`` | ``center_max`` / ``ribbon_right`` = on-screen full view, both side columns off | ``center_max_off``) overrides side visibility unless explicit flags are set."""
    mode: Optional[str] = None
    left_visible: Optional[bool] = None
    right_visible: Optional[bool] = None


class DisplayModeBody(BaseModel):
    """``mode``: ``windowed`` | ``exclusive_fullscreen`` | ``borderless_fullscreen`` (aliases: ``exclusive``, ``borderless``)."""
    mode: str
    width: Optional[int] = None
    height: Optional[int] = None
    refresh_rate_hz: Optional[int] = None


class RibbonTabBody(BaseModel):
    """Ribbon strip tab title (case-insensitive); add-ons use ``addon_<folderId>``."""
    tab: str


class CursorChromeBody(BaseModel):
    """At least one of ``lock_mode`` or ``visible`` should be set. ``lock_mode``: ``None`` | ``Locked`` | ``Confined``."""
    lock_mode: Optional[str] = None
    visible: Optional[bool] = None


class UiScaleBody(BaseModel):
    scale_multiplier: float


class UiTargetActiveBody(BaseModel):
    id: str
    active: bool


class StatusTextBody(BaseModel):
    message: str = ""
    text_is_eta: bool = False
    duration: float = 2.0
    progress_visibility: bool = False


class EventSystemBody(BaseModel):
    enabled: bool


class ViewCamerasEnabledCountBody(BaseModel):
    count: int

    @field_validator("count")
    @classmethod
    def count_non_negative(cls, v: int) -> int:
        if v < 0:
            raise ValueError("count must be >= 0")
        return v


class ViewCameraActiveBody(BaseModel):
    camera_index: int

    @field_validator("camera_index")
    @classmethod
    def camera_index_non_negative(cls, v: int) -> int:
        if v < 0:
            raise ValueError("camera_index must be >= 0")
        return v

    active: bool


class ViewCameraCurrentBody(BaseModel):
    camera_index: int

    @field_validator("camera_index")
    @classmethod
    def camera_index_non_negative(cls, v: int) -> int:
        if v < 0:
            raise ValueError("camera_index must be >= 0")
        return v


class ViewCameraProjectionBody(BaseModel):
    """At least one of ``orthographic``, ``orthographic_size``, ``field_of_view`` should be set."""
    camera_index: int

    @field_validator("camera_index")
    @classmethod
    def camera_index_non_negative(cls, v: int) -> int:
        if v < 0:
            raise ValueError("camera_index must be >= 0")
        return v

    orthographic: Optional[bool] = None
    orthographic_size: Optional[float] = None
    field_of_view: Optional[float] = None


class ProjectPath(BaseModel):
    filepath: str


class Import3DFileBody(BaseModel):
    """Absolute path to a mesh file on disk (FBX, OBJ, etc. — same as in-app Load model)."""

    filepath: str


class Export3DToPathBody(BaseModel):
    """Full path for the written mesh file; textures use the same path with extension removed + image ext."""

    mesh_filepath: str

class LoadAddonRequest(BaseModel):
    addon_id: str


class InvokeCallbackRequest(BaseModel):
    addon_id: str
    callback: str


class NotifyValueChangeRequest(BaseModel):
    addon_id: str
    element_id: str
    element_type: str
    value: Any = None


# --- Add-on UI (Unity AddonUI_MGR / ribbon tab content) — mirrors spz.ui.* JSON-RPC ---

class UICreatePanelBody(BaseModel):
    addon_id: str
    title: str = "Add-on Panel"


class UIAddButtonBody(BaseModel):
    addon_id: str
    panel_id: str = ""
    label: str = "Button"
    callback: str = ""


class UIAddToggleBody(BaseModel):
    addon_id: str
    panel_id: str = ""
    label: str = "Toggle"
    default: bool = False
    callback: str | None = None


class UIAddSliderBody(BaseModel):
    addon_id: str
    panel_id: str = ""
    label: str = "Slider"
    min: float = 0
    max: float = 100
    default: float = 50


class UIAddTextInputBody(BaseModel):
    addon_id: str
    panel_id: str = ""
    label: str = "Text Input"
    default: str = ""


class UIAddDropdownBody(BaseModel):
    addon_id: str
    panel_id: str = ""
    label: str = "Dropdown"
    options: List[str] = []
    default: int = 0


class UISetValueBody(BaseModel):
    element_id: str
    value: Any = None


class CameraFovBody(BaseModel):
    fov: float


class UIApplyThemeBody(BaseModel):
    theme_id: str
    tokens: Optional[Dict[str, Any]] = None
    mode: Optional[str] = None


class UISetLineIconBody(BaseModel):
    tab: str
    icon: str


class UIRegisterThemeBody(BaseModel):
    theme_id: str
    tokens: Dict[str, Any]
    label: Optional[str] = None
    owner: Optional[str] = None


class UIUnregisterThemeBody(BaseModel):
    theme_id: str


class PaintFloat01Body(BaseModel):
    """Brush scalar in 0..1 (size, spacing, roundness, opacity). Use ``value`` or the alias key matching Unity params."""
    value: float


class PaintAngleBody(BaseModel):
    """Brush angle in degrees (any finite value; same as ribbon)."""
    value: float


class PaintIndexBody(BaseModel):
    """Integer index (active paint layer or brush stamp)."""
    index: int


class SdWorkflowModeBody(BaseModel):
    """``WorkflowRibbon_CurrMode`` name, e.g. ``Inpaint_Color``, ``ProjectionsMasking``."""
    mode: str


class SdBoolValueBody(BaseModel):
    value: bool


class SdFloatValueBody(BaseModel):
    value: float


class SdSkyboxColorBody(BaseModel):
    is_top: bool = True
    r: float
    g: float
    b: float
    a: float = 1.0


class MeshVisibilityBody(BaseModel):
    visible: bool


class _UnityCallError(Exception):
    """Raised from asyncio.to_thread worker for Unity RPC failures; converted to HTTPException on the event loop."""

    def __init__(self, status_code: int, detail: str):
        self.status_code = status_code
        self.detail = detail
        super().__init__(detail)


def _call_unity_sync(method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """Blocking JSON-RPC to Unity (socket I/O). Do not raise HTTPException here — runs inside a thread pool."""
    if _api is None:
        raise _UnityCallError(503, "Not connected to Unity")
    try:
        client = _api._client
        return client._send_request(method, params or {})
    except _UnityCallError:
        raise
    except Exception as e:
        raise _UnityCallError(500, str(e))


async def call_unity_async(method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """Run blocking Unity RPC in a thread pool so the event loop is not stalled on socket I/O."""
    try:
        return await asyncio.to_thread(_call_unity_sync, method, params)
    except _UnityCallError as e:
        raise HTTPException(status_code=e.status_code, detail=e.detail)


async def _connection_ready_async() -> bool:
    """
    Run the registered readiness probe (typically a Unity JSON-RPC ping) in a worker thread.
    Callers must ensure _connection_ready_callback is set (non-None) before awaiting.
    """
    cb = _connection_ready_callback
    if cb is None:
        raise RuntimeError("connection ready callback not set")
    return bool(await asyncio.to_thread(cb))

# ============================================
# Meta / context (add-on discovery — same JSON-RPC as TCP)
# ============================================

@app.get("/api/v1/meta", tags=["meta"])
async def api_meta():
    """
    Supported ``spz.cmd.*`` / ``spz.ui.*`` method names and ``addon_rpc_version``.
    Unity serves this even while meshes/cameras are still initializing (no FastPath required).
    """
    return await call_unity_async("spz.cmd.get_api_capabilities", {})


@app.get("/api/v1/context", tags=["meta"])
async def api_context():
    """
    Single snapshot: selection, workflow, SD/gen3d flags, project paths, nested ``brush``,
    ``paint_layers``, and ``sd_workflow`` (requires FastPath ready).
    """
    return await call_unity_async("spz.cmd.get_addon_context", {})


@app.get("/api/v1/editor/layout", tags=["editor"])
async def get_editor_layout():
    """Left/right panel visibility and whether the center viewport span is expanded (no FastPath required)."""
    return await call_unity_async("spz.cmd.get_editor_layout", {})


@app.post("/api/v1/editor/layout", tags=["editor"])
async def set_editor_layout(body: EditorLayoutBody):
    """Set side column visibility; use ``mode=viewport_focus`` to collapse both sides (wide center viewport)."""
    params: Dict[str, Any] = {}
    if body.mode is not None:
        params["mode"] = body.mode
    if body.left_visible is not None:
        params["left_visible"] = body.left_visible
    if body.right_visible is not None:
        params["right_visible"] = body.right_visible
    return await call_unity_async("spz.cmd.set_editor_layout", params)


@app.get("/api/v1/display/mode", tags=["display"])
async def get_display_mode():
    """Current fullscreen flag, Unity ``FullScreenMode`` name, resolution, primary display size."""
    return await call_unity_async("spz.cmd.get_display_mode", {})


@app.post("/api/v1/display/mode", tags=["display"])
async def set_display_mode(body: DisplayModeBody):
    """Switch windowed, OS exclusive fullscreen, or borderless fullscreen; optional resolution and refresh (exclusive)."""
    params: Dict[str, Any] = {"mode": body.mode}
    if body.width is not None:
        params["width"] = int(body.width)
    if body.height is not None:
        params["height"] = int(body.height)
    if body.refresh_rate_hz is not None:
        params["refresh_rate_hz"] = int(body.refresh_rate_hz)
    return await call_unity_async("spz.cmd.set_display_mode", params)


@app.get("/api/v1/chrome/ribbon/tabs", tags=["chrome"])
async def get_ribbon_tabs():
    """Titles of command-ribbon tabs (built-in + add-on)."""
    return await call_unity_async("spz.cmd.get_ribbon_tabs", {})


@app.post("/api/v1/chrome/ribbon/tab", tags=["chrome"])
async def set_ribbon_tab(body: RibbonTabBody):
    """Activate a ribbon tab by title (same strings as ``GET .../ribbon/tabs``)."""
    return await call_unity_async("spz.cmd.set_ribbon_tab", {"tab": body.tab})


@app.get("/api/v1/chrome/cursor", tags=["chrome"])
async def get_cursor_chrome():
    """Cursor lock mode and visibility."""
    return await call_unity_async("spz.cmd.get_cursor_state", {})


@app.post("/api/v1/chrome/cursor", tags=["chrome"])
async def set_cursor_chrome(body: CursorChromeBody):
    """Set cursor lock (None/Locked/Confined) and/or visibility."""
    if body.lock_mode is None and body.visible is None:
        raise HTTPException(status_code=400, detail="Provide lock_mode and/or visible")
    params: Dict[str, Any] = {}
    if body.lock_mode is not None:
        params["lock_mode"] = body.lock_mode
    if body.visible is not None:
        params["visible"] = body.visible
    return await call_unity_async("spz.cmd.set_cursor_state", params)


@app.get("/api/v1/chrome/ui-scale", tags=["chrome"])
async def get_ui_scale():
    """Skeleton canvas UI scale (Scale With Screen Size reference resolution)."""
    return await call_unity_async("spz.cmd.get_ui_scale", {})


@app.post("/api/v1/chrome/ui-scale", tags=["chrome"])
async def set_ui_scale(body: UiScaleBody):
    """``scale_multiplier`` 1 = baseline; higher enlarges UI (clamped ~0.5–2)."""
    return await call_unity_async("spz.cmd.set_ui_scale", {"scale_multiplier": float(body.scale_multiplier)})


@app.get("/api/v1/chrome/ui-targets", tags=["chrome"])
async def list_ui_targets():
    """Named GameObject ids for show/hide (built-in + optional registry)."""
    return await call_unity_async("spz.cmd.list_ui_targets", {})


@app.get("/api/v1/chrome/ui-targets/{target_id}/active", tags=["chrome"])
async def get_ui_target_active(target_id: str):
    return await call_unity_async("spz.cmd.get_ui_target_active", {"id": target_id})


@app.post("/api/v1/chrome/ui-targets/active", tags=["chrome"])
async def set_ui_target_active(body: UiTargetActiveBody):
    return await call_unity_async("spz.cmd.set_ui_target_active", {"id": body.id, "active": body.active})


@app.post("/api/v1/chrome/status-text", tags=["chrome"])
async def show_status_text(body: StatusTextBody):
    """Viewport status line (same pipeline as in-app tips)."""
    return await call_unity_async("spz.cmd.show_status_text", {
        "message": body.message,
        "text_is_eta": body.text_is_eta,
        "duration": float(body.duration),
        "progress_visibility": body.progress_visibility,
    })


@app.get("/api/v1/chrome/event-system", tags=["chrome"])
async def get_event_system_chrome():
    return await call_unity_async("spz.cmd.get_event_system", {})


@app.post("/api/v1/chrome/event-system", tags=["chrome"])
async def set_event_system_chrome(body: EventSystemBody):
    return await call_unity_async("spz.cmd.set_event_system", {"enabled": body.enabled})

# ============================================
# Camera Endpoints
# ============================================

@app.get("/api/v1/cameras/{camera_id}/position")
async def get_camera_position(camera_id: int):
    """Get camera position"""
    result = await call_unity_async("spz.cmd.get_camera_pos", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0)
        }
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/position")
async def set_camera_position(camera_id: int, position: Position):
    """Set camera position"""
    result = await call_unity_async("spz.cmd.set_camera_pos", {
        "camera_index": camera_id,
        "x": position.x,
        "y": position.y,
        "z": position.z
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/{camera_id}/rotation")
async def get_camera_rotation(camera_id: int):
    """Get camera rotation"""
    result = await call_unity_async("spz.cmd.get_camera_rot", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0),
            "w": result.get("w", 1.0)
        }
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/rotation")
async def set_camera_rotation(camera_id: int, rotation: Rotation):
    """Set camera rotation"""
    result = await call_unity_async("spz.cmd.set_camera_rot", {
        "camera_index": camera_id,
        "x": rotation.x,
        "y": rotation.y,
        "z": rotation.z,
        "w": rotation.w
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/{camera_id}/fov")
async def get_camera_fov(camera_id: int):
    """Get camera FOV"""
    result = await call_unity_async("spz.cmd.get_camera_fov", {"camera_index": camera_id})
    if "success" in result and result["success"]:
        return {"fov": result.get("fov", 60.0)}
    raise HTTPException(status_code=404, detail="Camera not found")

@app.post("/api/v1/cameras/{camera_id}/fov")
async def set_camera_fov(camera_id: int, body: CameraFovBody):
    """Set camera FOV (JSON body ``{\"fov\": ...}``, not a query param)."""
    result = await call_unity_async("spz.cmd.set_camera_fov", {
        "camera_index": camera_id,
        "fov": float(body.fov)
    })
    return {"success": result.get("success", False)}

@app.get("/api/v1/cameras/positions")
async def get_all_camera_positions():
    """Get all camera positions"""
    result = await call_unity_async("spz.cmd.get_all_camera_positions", {})
    return result

@app.get("/api/v1/cameras/rotations")
async def get_all_camera_rotations():
    """Get all camera rotations"""
    result = await call_unity_async("spz.cmd.get_all_camera_rotations", {})
    return result

@app.get("/api/v1/cameras/fovs")
async def get_all_camera_fovs():
    """Get all camera FOVs"""
    result = await call_unity_async("spz.cmd.get_all_camera_fovs", {})
    return result


@app.get("/api/v1/view-cameras/state", tags=["cameras"])
async def get_view_cameras_state():
    """Per-slot active flags, current index, and count (layered composite, not a 2x2 grid)."""
    return await call_unity_async("spz.cmd.get_view_cameras", {})


@app.post("/api/v1/view-cameras/enabled-count", tags=["cameras"])
async def set_view_cameras_enabled_count(body: ViewCamerasEnabledCountBody):
    """Enable the first ``count`` view cameras, disable the rest."""
    return await call_unity_async(
        "spz.cmd.set_view_cameras_enabled_count", {"count": int(body.count)})


@app.post("/api/v1/view-cameras/active", tags=["cameras"])
async def set_view_camera_active(body: ViewCameraActiveBody):
    return await call_unity_async(
        "spz.cmd.set_view_camera_active",
        {"camera_index": int(body.camera_index), "active": bool(body.active)},
    )


@app.post("/api/v1/view-cameras/current", tags=["cameras"])
async def set_current_view_camera(body: ViewCameraCurrentBody):
    return await call_unity_async(
        "spz.cmd.set_current_view_camera", {"camera_index": int(body.camera_index)})


@app.get("/api/v1/view-cameras/{camera_index}/projection", tags=["cameras"])
async def get_view_camera_projection(camera_index: int):
    return await call_unity_async("spz.cmd.get_view_camera_projection", {"camera_index": camera_index})


@app.post("/api/v1/view-cameras/projection", tags=["cameras"])
async def set_view_camera_projection(body: ViewCameraProjectionBody):
    params: Dict[str, Any] = {"camera_index": int(body.camera_index)}
    if body.orthographic is not None:
        params["orthographic"] = body.orthographic
    if body.orthographic_size is not None:
        params["orthographic_size"] = float(body.orthographic_size)
    if body.field_of_view is not None:
        params["field_of_view"] = float(body.field_of_view)
    if len(params) <= 1:
        raise HTTPException(
            status_code=400,
            detail="Set at least one of orthographic, orthographic_size, field_of_view",
        )
    return await call_unity_async("spz.cmd.set_view_camera_projection", params)


@app.get("/api/v1/projection/cameras/count", tags=["projection"])
async def projection_camera_count():
    """Number of projection cameras in the stack."""
    return await call_unity_async("spz.cmd.get_projection_camera_count", {})


@app.get("/api/v1/projection/cameras/{camera_index}/position", tags=["projection"])
async def projection_get_camera_position(camera_index: int):
    result = await call_unity_async("spz.cmd.get_projection_camera_pos", {"camera_index": camera_index})
    if result.get("success"):
        return {"x": result.get("x", 0.0), "y": result.get("y", 0.0), "z": result.get("z", 0.0)}
    raise HTTPException(status_code=404, detail="Projection camera not found")


@app.post("/api/v1/projection/cameras/{camera_index}/position", tags=["projection"])
async def projection_set_camera_position(camera_index: int, position: Position):
    result = await call_unity_async(
        "spz.cmd.set_projection_camera_pos",
        {"camera_index": camera_index, "x": position.x, "y": position.y, "z": position.z},
    )
    return {"success": result.get("success", False)}


@app.get("/api/v1/projection/cameras/{camera_index}/rotation", tags=["projection"])
async def projection_get_camera_rotation(camera_index: int):
    result = await call_unity_async("spz.cmd.get_projection_camera_rot", {"camera_index": camera_index})
    if result.get("success"):
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0),
            "w": result.get("w", 1.0),
        }
    raise HTTPException(status_code=404, detail="Projection camera not found")


@app.post("/api/v1/projection/cameras/{camera_index}/rotation", tags=["projection"])
async def projection_set_camera_rotation(camera_index: int, rotation: Rotation):
    result = await call_unity_async(
        "spz.cmd.set_projection_camera_rot",
        {
            "camera_index": camera_index,
            "x": rotation.x,
            "y": rotation.y,
            "z": rotation.z,
            "w": rotation.w,
        },
    )
    return {"success": result.get("success", False)}

# ============================================
# Mesh Endpoints
# ============================================

@app.get("/api/v1/meshes")
async def get_meshes():
    """Get all mesh IDs"""
    result = await call_unity_async("spz.cmd.get_all_mesh_ids", {})
    return result


@app.get("/api/v1/meshes/selected", tags=["meshes"])
async def get_selected_mesh_ids():
    """Currently selected mesh IDs (same as ``spz.cmd.get_selected_meshes``)."""
    return await call_unity_async("spz.cmd.get_selected_meshes", {})


@app.post("/api/v1/meshes/{mesh_id}/select", tags=["meshes"])
async def mesh_select(mesh_id: int):
    result = await call_unity_async("spz.cmd.select_mesh", {"mesh_id": mesh_id})
    return result


@app.post("/api/v1/meshes/{mesh_id}/deselect", tags=["meshes"])
async def mesh_deselect(mesh_id: int):
    result = await call_unity_async("spz.cmd.deselect_mesh", {"mesh_id": mesh_id})
    return result


@app.get("/api/v1/meshes/{mesh_id}/position")
async def get_mesh_position(mesh_id: int):
    """Get mesh position"""
    result = await call_unity_async("spz.cmd.get_mesh_pos", {"mesh_id": mesh_id})
    if "success" in result and result["success"]:
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0)
        }
    raise HTTPException(status_code=404, detail="Mesh not found")

@app.post("/api/v1/meshes/{mesh_id}/position")
async def set_mesh_position(mesh_id: int, position: Position):
    """Set mesh position"""
    result = await call_unity_async("spz.cmd.set_mesh_pos", {
        "mesh_id": mesh_id,
        "x": position.x,
        "y": position.y,
        "z": position.z
    })
    return {"success": result.get("success", False)}

@app.post("/api/v1/meshes/batch/position")
async def set_mesh_positions_batch(request: Dict[str, Any]):
    """Set multiple mesh positions (batch operation)"""
    mesh_ids = request.get("mesh_ids", [])
    positions = request.get("positions", [])
    result = await call_unity_async("spz.cmd.set_mesh_positions", {
        "mesh_ids": mesh_ids,
        "positions": positions
    })
    return result


@app.post("/api/v1/meshes/batch/rotation", tags=["meshes"])
async def set_mesh_rotations_batch(request: Dict[str, Any]):
    """Batch set mesh rotations (quaternions); same payload shape as TCP ``mesh_ids`` + ``rotations``."""
    mesh_ids = request.get("mesh_ids", [])
    rotations = request.get("rotations", [])
    return await call_unity_async("spz.cmd.set_mesh_rotations", {"mesh_ids": mesh_ids, "rotations": rotations})


@app.post("/api/v1/meshes/batch/scale", tags=["meshes"])
async def set_mesh_scales_batch(request: Dict[str, Any]):
    """Batch set mesh scales; same payload shape as TCP ``mesh_ids`` + ``scales``."""
    mesh_ids = request.get("mesh_ids", [])
    scales = request.get("scales", [])
    return await call_unity_async("spz.cmd.set_mesh_scales", {"mesh_ids": mesh_ids, "scales": scales})


@app.get("/api/v1/meshes/{mesh_id}/rotation", tags=["meshes"])
async def get_mesh_rotation(mesh_id: int):
    result = await call_unity_async("spz.cmd.get_mesh_rot", {"mesh_id": mesh_id})
    if result.get("success"):
        return {
            "x": result.get("x", 0.0),
            "y": result.get("y", 0.0),
            "z": result.get("z", 0.0),
            "w": result.get("w", 1.0),
        }
    raise HTTPException(status_code=404, detail="Mesh not found")


@app.post("/api/v1/meshes/{mesh_id}/rotation", tags=["meshes"])
async def set_mesh_rotation(mesh_id: int, rotation: Rotation):
    result = await call_unity_async(
        "spz.cmd.set_mesh_rot",
        {
            "mesh_id": mesh_id,
            "x": rotation.x,
            "y": rotation.y,
            "z": rotation.z,
            "w": rotation.w,
        },
    )
    return {"success": result.get("success", False)}


@app.get("/api/v1/meshes/{mesh_id}/scale", tags=["meshes"])
async def get_mesh_scale(mesh_id: int):
    result = await call_unity_async("spz.cmd.get_mesh_scale", {"mesh_id": mesh_id})
    if result.get("success"):
        return {"x": result.get("x", 1.0), "y": result.get("y", 1.0), "z": result.get("z", 1.0)}
    raise HTTPException(status_code=404, detail="Mesh not found")


@app.post("/api/v1/meshes/{mesh_id}/scale", tags=["meshes"])
async def set_mesh_scale(mesh_id: int, scale: Position):
    """Scale uses x,y,z like position (Unity ``Vector3`` scale)."""
    result = await call_unity_async(
        "spz.cmd.set_mesh_scale",
        {"mesh_id": mesh_id, "x": scale.x, "y": scale.y, "z": scale.z},
    )
    return {"success": result.get("success", False)}


@app.get("/api/v1/meshes/{mesh_id}/bounds", tags=["meshes"])
async def get_mesh_bounds(mesh_id: int):
    result = await call_unity_async("spz.cmd.get_mesh_bounds", {"mesh_id": mesh_id})
    if result.get("success"):
        return {
            "center": {
                "x": result.get("center_x", 0.0),
                "y": result.get("center_y", 0.0),
                "z": result.get("center_z", 0.0),
            },
            "size": {
                "x": result.get("size_x", 0.0),
                "y": result.get("size_y", 0.0),
                "z": result.get("size_z", 0.0),
            },
        }
    raise HTTPException(status_code=404, detail="Mesh not found")


@app.get("/api/v1/meshes/{mesh_id}/visibility", tags=["meshes"])
async def get_mesh_visibility(mesh_id: int):
    result = await call_unity_async("spz.cmd.get_mesh_visibility", {"mesh_id": mesh_id})
    if result.get("success"):
        return {"visible": result.get("visible", True)}
    raise HTTPException(status_code=404, detail="Mesh not found")


@app.post("/api/v1/meshes/{mesh_id}/visibility", tags=["meshes"])
async def set_mesh_visibility(mesh_id: int, body: MeshVisibilityBody):
    result = await call_unity_async(
        "spz.cmd.set_mesh_visibility",
        {"mesh_id": mesh_id, "visible": body.visible},
    )
    return {"success": result.get("success", False)}


@app.get("/api/v1/meshes/{mesh_id}/name", tags=["meshes"])
async def get_mesh_name(mesh_id: int):
    result = await call_unity_async("spz.cmd.get_mesh_name", {"mesh_id": mesh_id})
    if result.get("success"):
        return {"name": result.get("name", "")}
    raise HTTPException(status_code=404, detail="Mesh not found")

# ============================================
# Scene Endpoints
# ============================================

@app.get("/api/v1/scene/info")
async def get_scene_info():
    """Get scene information"""
    total, selected = await asyncio.gather(
        call_unity_async("spz.cmd.get_total_mesh_count", {}),
        call_unity_async("spz.cmd.get_selected_mesh_count", {}),
    )
    return {
        "total_meshes": total.get("count", 0),
        "selected_meshes": selected.get("count", 0)
    }


@app.get("/api/v1/scene/selected_bounds", tags=["scene"])
async def get_scene_selected_bounds():
    """Axis-aligned bounds union of all selected meshes (``center`` + ``size``)."""
    result = await call_unity_async("spz.cmd.get_selected_meshes_bounds", {})
    if result.get("success"):
        return {
            "center": {
                "x": result.get("center_x", 0.0),
                "y": result.get("center_y", 0.0),
                "z": result.get("center_z", 0.0),
            },
            "size": {
                "x": result.get("size_x", 0.0),
                "y": result.get("size_y", 0.0),
                "z": result.get("size_z", 0.0),
            },
        }
    raise HTTPException(status_code=404, detail="No bounds for current selection")

@app.post("/api/v1/scene/select_all")
async def select_all_meshes():
    """Select all meshes"""
    result = await call_unity_async("spz.cmd.select_all_meshes", {})
    return result

@app.post("/api/v1/scene/deselect_all")
async def deselect_all_meshes():
    """Deselect all meshes"""
    result = await call_unity_async("spz.cmd.deselect_all_meshes", {})
    return result

# ============================================
# Stable Diffusion Endpoints
# ============================================

@app.get("/api/v1/sd/prompt")
async def get_sd_prompt():
    """Get Stable Diffusion prompts"""
    positive, negative = await asyncio.gather(
        call_unity_async("spz.cmd.get_positive_prompt", {}),
        call_unity_async("spz.cmd.get_negative_prompt", {}),
    )
    return {
        "positive": positive.get("prompt", ""),
        "negative": negative.get("prompt", "")
    }

@app.post("/api/v1/sd/prompt")
async def set_sd_prompt(prompt: Prompt):
    """Set Stable Diffusion prompts"""
    results = {}
    tasks = []
    keys = []
    if prompt.positive is not None:
        keys.append("positive")
        tasks.append(call_unity_async("spz.cmd.set_positive_prompt", {"prompt": prompt.positive}))
    if prompt.negative is not None:
        keys.append("negative")
        tasks.append(call_unity_async("spz.cmd.set_negative_prompt", {"prompt": prompt.negative}))
    if tasks:
        for k, r in zip(keys, await asyncio.gather(*tasks)):
            results[k] = r
    return results

@app.post("/api/v1/sd/generate")
async def trigger_sd_generation(is_background: bool = False):
    """Trigger Stable Diffusion texture generation (``is_background`` = backgrounds pass)."""
    result = await call_unity_async("spz.cmd.trigger_texture_generation", {"is_background": is_background})
    return result


@app.get("/api/v1/sd/workflow/mode", tags=["sd"])
async def sd_get_workflow_mode():
    return await call_unity_async("spz.cmd.get_workflow_mode", {})


@app.post("/api/v1/sd/workflow/mode", tags=["sd"])
async def sd_set_workflow_mode(body: SdWorkflowModeBody):
    return await call_unity_async("spz.cmd.set_workflow_mode", {"mode": body.mode})


@app.get("/api/v1/sd/generation/options", tags=["sd"])
async def sd_get_generation_options():
    """Forge/WebUI-aligned snapshot: denoising, mask blur, soft/tileable/ignore-depth, edge sliders, workflow mode, ``sd_connected``."""
    return await call_unity_async("spz.cmd.get_sd_workflow_options", {})


@app.post("/api/v1/sd/generation/denoising", tags=["sd"])
async def sd_set_denoising(body: SdFloatValueBody):
    return await call_unity_async("spz.cmd.set_sd_denoising_strength", {"value": body.value})


@app.post("/api/v1/sd/generation/mask_blur", tags=["sd"])
async def sd_set_mask_blur(body: SdFloatValueBody):
    return await call_unity_async("spz.cmd.set_sd_mask_blur", {"value": body.value})


@app.post("/api/v1/sd/generation/soft_inpaint", tags=["sd"])
async def sd_set_soft_inpaint(body: SdBoolValueBody):
    return await call_unity_async("spz.cmd.set_sd_soft_inpaint", {"value": body.value})


@app.post("/api/v1/sd/generation/tileable", tags=["sd"])
async def sd_set_tileable(body: SdBoolValueBody):
    return await call_unity_async("spz.cmd.set_sd_tileable_inpaint", {"value": body.value})


@app.post("/api/v1/sd/generation/ignore_depth_or_normals", tags=["sd"])
async def sd_set_ignore_depth_or_normals(body: SdBoolValueBody):
    return await call_unity_async("spz.cmd.set_sd_ignore_depth_or_normals", {"value": body.value})


@app.get("/api/v1/sd/controlnet/summary", tags=["sd"])
async def sd_controlnet_summary():
    total, active = await asyncio.gather(
        call_unity_async("spz.cmd.get_controlnet_unit_count", {}),
        call_unity_async("spz.cmd.get_active_controlnet_unit_count", {}),
    )
    return {
        "total_units": total.get("count", 0),
        "active_units": active.get("count", 0),
    }


@app.get("/api/v1/sd/controlnet/{unit_index}/enabled", tags=["sd"])
async def sd_controlnet_get_enabled(unit_index: int):
    return await call_unity_async("spz.cmd.get_controlnet_unit_enabled", {"unit_index": unit_index})


@app.post("/api/v1/sd/controlnet/{unit_index}/enabled", tags=["sd"])
async def sd_controlnet_set_enabled(unit_index: int, body: SdBoolValueBody):
    return await call_unity_async(
        "spz.cmd.set_controlnet_unit_enabled",
        {"unit_index": unit_index, "enabled": body.value},
    )


@app.get("/api/v1/sd/controlnet/{unit_index}/weight", tags=["sd"])
async def sd_controlnet_get_weight(unit_index: int):
    return await call_unity_async("spz.cmd.get_controlnet_unit_weight", {"unit_index": unit_index})


@app.post("/api/v1/sd/controlnet/{unit_index}/weight", tags=["sd"])
async def sd_controlnet_set_weight(unit_index: int, body: SdFloatValueBody):
    return await call_unity_async(
        "spz.cmd.set_controlnet_unit_weight",
        {"unit_index": unit_index, "weight": body.value},
    )


@app.get("/api/v1/sd/controlnet/{unit_index}/model", tags=["sd"])
async def sd_controlnet_get_model(unit_index: int):
    return await call_unity_async("spz.cmd.get_controlnet_unit_model", {"unit_index": unit_index})


@app.get("/api/v1/sd/skybox", tags=["sd"])
async def sd_get_skybox_colors():
    top_c, bot_c, is_clear = await asyncio.gather(
        call_unity_async("spz.cmd.get_skybox_top_color", {}),
        call_unity_async("spz.cmd.get_skybox_bottom_color", {}),
        call_unity_async("spz.cmd.is_skybox_gradient_clear", {}),
    )
    return {
        "gradient_clear": is_clear.get("is_clear", False),
        "top": {k: top_c.get(k) for k in ("r", "g", "b", "a")} if top_c.get("success") else None,
        "bottom": {k: bot_c.get(k) for k in ("r", "g", "b", "a")} if bot_c.get("success") else None,
    }


@app.post("/api/v1/sd/skybox/color", tags=["sd"])
async def sd_set_skybox_color(body: SdSkyboxColorBody):
    return await call_unity_async(
        "spz.cmd.set_skybox_color",
        {
            "is_top": body.is_top,
            "r": body.r,
            "g": body.g,
            "b": body.b,
            "a": body.a,
        },
    )


@app.get("/api/v1/sd/status")
async def get_sd_status():
    """Get Stable Diffusion status"""
    is_generating, is_connected = await asyncio.gather(
        call_unity_async("spz.cmd.is_generating", {}),
        call_unity_async("spz.cmd.is_sd_connected", {}),
    )
    return {
        "generating": is_generating.get("generating", False),
        "connected": is_connected.get("connected", False)
    }

@app.post("/api/v1/sd/stop")
async def stop_sd_generation():
    """Stop Stable Diffusion generation"""
    result = await call_unity_async("spz.cmd.stop_generation", {})
    return result

# ============================================
# Project Endpoints
# ============================================

@app.get("/api/v1/project/info")
async def get_project_info():
    """
    Get project information. Unity ``spz.cmd.get_project_path`` returns ``path`` only (never ``filepath``).
    When no project is saved, ``path`` is unavailable, but ``data_dir`` can still be set to a
    per-machine session folder (``data_dir_is_session: true``) so SPZ GO/Blender exchange works without a saved .spz.
    """
    path, version, data_dir = await asyncio.gather(
        call_unity_async("spz.cmd.get_project_path", {}),
        call_unity_async("spz.cmd.get_project_version", {}),
        call_unity_async("spz.cmd.get_project_data_dir", {}),
    )
    if not version.get("success"):
        raise HTTPException(
            status_code=502,
            detail="spz.cmd.get_project_version did not return a version",
        )
    ver = version.get("version")
    if ver is None:
        raise HTTPException(
            status_code=502,
            detail="spz.cmd.get_project_version succeeded but omitted 'version'",
        )

    path_ok = bool(path.get("success"))
    if path_ok and path.get("path") is None:
        raise HTTPException(
            status_code=502,
            detail="spz.cmd.get_project_path succeeded but omitted 'path' (expected Unity contract)",
        )
    path_value = path.get("path") if path_ok else None

    dd_ok = bool(data_dir.get("success"))
    if dd_ok and data_dir.get("data_dir") is None:
        raise HTTPException(
            status_code=502,
            detail="spz.cmd.get_project_data_dir succeeded but omitted 'data_dir'",
        )
    dd_value = data_dir.get("data_dir") if dd_ok else None
    dd_is_session = bool(data_dir.get("data_dir_is_session")) if isinstance(data_dir, dict) else False

    return {
        "path": path_value,
        "path_available": path_ok,
        "version": ver,
        "data_dir": dd_value,
        "data_dir_available": dd_ok,
        "data_dir_is_session": dd_is_session,
    }


@app.get("/api/v1/project/operation_in_progress", tags=["project"])
async def get_project_operation_in_progress():
    """True while Unity save/load dialog workflow is in progress."""
    return await call_unity_async("spz.cmd.is_project_operation_in_progress", {})

@app.post("/api/v1/project/save")
async def save_project(project_path: ProjectPath):
    """Save project"""
    result = await call_unity_async("spz.cmd.save_project", {
        "filepath": project_path.filepath
    })
    return result

@app.post("/api/v1/project/load")
async def load_project(project_path: ProjectPath):
    """Load project"""
    result = await call_unity_async("spz.cmd.load_project", {
        "filepath": project_path.filepath
    })
    return result

# ============================================
# 3D generation (external 3D pipeline — same JSON-RPC as TCP)
# ============================================

@app.get("/api/v1/gen3d/connected", tags=["gen3d"])
async def gen3d_connected():
    return await call_unity_async("spz.cmd.is_3d_connected", {})


@app.get("/api/v1/gen3d/ready", tags=["gen3d"])
async def gen3d_ready():
    return await call_unity_async("spz.cmd.is_3d_generation_ready", {})


@app.get("/api/v1/gen3d/in_progress", tags=["gen3d"])
async def gen3d_in_progress():
    return await call_unity_async("spz.cmd.is_3d_generation_in_progress", {})


@app.post("/api/v1/gen3d/trigger", tags=["gen3d"])
async def gen3d_trigger():
    return await call_unity_async("spz.cmd.trigger_3d_generation", {})

# ============================================
# Export (Unity menu actions — may open dialogs / block briefly)
# ============================================

@app.post("/api/v1/export/3d_with_textures", tags=["export"])
async def export_3d_with_textures():
    return await call_unity_async("spz.cmd.export_3d_with_textures", {})


@app.post("/api/v1/meshes/import", tags=["meshes"])
async def import_3d_model(body: Import3DFileBody):
    """Load a 3D model from an absolute file path (no OS dialog; same Assimp path as the app)."""
    return await call_unity_async("spz.cmd.import_3d_model", {"filepath": body.filepath})


@app.post("/api/v1/export/3d_to_path", tags=["export"])
async def export_3d_with_textures_to_path(body: Export3DToPathBody):
    """Export current scene mesh + texture pack to a known file path (no save dialog)."""
    return await call_unity_async(
        "spz.cmd.export_3d_with_textures_to_path",
        {"mesh_filepath": body.mesh_filepath},
    )


@app.post("/api/v1/export/projection_textures", tags=["export"])
async def export_projection_textures(is_dilate: bool = True):
    """``is_dilate`` matches Unity ``ExportProjectionTextures`` default."""
    return await call_unity_async("spz.cmd.export_projection_textures", {"is_dilate": is_dilate})


@app.post("/api/v1/export/view_textures", tags=["export"])
async def export_view_textures():
    return await call_unity_async("spz.cmd.export_view_textures", {})

# ============================================
# Paint / brush (HTTP mirror of spz.cmd.* — viewport brush + inpaint layer stack)
# ============================================

@app.get("/api/v1/paint/brush/settings", tags=["paint"])
async def paint_get_brush_settings():
    """Snapshot: size01, spacing01, angle_deg, roundness01, opacity01, brush_index, brush_count, brush_name, symmetry flags."""
    return await call_unity_async("spz.cmd.get_brush_settings", {})


@app.get("/api/v1/paint/layers", tags=["paint"])
async def paint_get_layers():
    """Inpaint layer stack: active_index and layers[{index, name, visible, opacity}]."""
    return await call_unity_async("spz.cmd.get_paint_layers", {})


@app.post("/api/v1/paint/brush/size", tags=["paint"])
async def paint_set_brush_size(body: PaintFloat01Body):
    return await call_unity_async("spz.cmd.set_brush_size", {"value": body.value})


@app.post("/api/v1/paint/brush/spacing", tags=["paint"])
async def paint_set_brush_spacing(body: PaintFloat01Body):
    return await call_unity_async("spz.cmd.set_brush_spacing", {"value": body.value})


@app.post("/api/v1/paint/brush/angle", tags=["paint"])
async def paint_set_brush_angle(body: PaintAngleBody):
    return await call_unity_async("spz.cmd.set_brush_angle", {"value": body.value})


@app.post("/api/v1/paint/brush/roundness", tags=["paint"])
async def paint_set_brush_roundness(body: PaintFloat01Body):
    return await call_unity_async("spz.cmd.set_brush_roundness", {"value": body.value})


@app.post("/api/v1/paint/brush/opacity", tags=["paint"])
async def paint_set_brush_opacity(body: PaintFloat01Body):
    return await call_unity_async("spz.cmd.set_brush_opacity", {"value": body.value})


@app.post("/api/v1/paint/brush/stamp_index", tags=["paint"])
async def paint_set_brush_stamp_index(body: PaintIndexBody):
    """Select brush alpha by index (0–2 built-in soft/medium/hard; 3+ custom / ABR)."""
    return await call_unity_async("spz.cmd.set_brush_stamp_index", {"index": body.index})


@app.post("/api/v1/paint/layers/active", tags=["paint"])
async def paint_set_active_layer(body: PaintIndexBody):
    """Set which inpaint layer receives new strokes."""
    return await call_unity_async("spz.cmd.set_active_paint_layer", {"index": body.index})

# ============================================
# Add-on UI (HTTP mirror of spz.ui.* — external tools / services without raw TCP)
# ============================================

@app.post("/api/v1/ui/panel", tags=["ui"])
async def ui_create_panel(body: UICreatePanelBody):
    """Create ribbon tab + panel shell for an add-on (same as Python api.ui.create_panel)."""
    return await call_unity_async("spz.ui.create_panel", {
        "addon_id": body.addon_id,
        "title": body.title,
    })


@app.post("/api/v1/ui/button", tags=["ui"])
async def ui_add_button(body: UIAddButtonBody):
    return await call_unity_async("spz.ui.add_button", {
        "addon_id": body.addon_id,
        "panel_id": body.panel_id,
        "label": body.label,
        "callback": body.callback,
    })


@app.post("/api/v1/ui/toggle", tags=["ui"])
async def ui_add_toggle(body: UIAddToggleBody):
    """Add a checkbox-style toggle to an add-on panel (rpc 1.14+)."""
    params = {
        "addon_id": body.addon_id,
        "panel_id": body.panel_id,
        "label": body.label,
        "default": body.default,
    }
    if body.callback:
        params["callback"] = body.callback
    return await call_unity_async("spz.ui.add_toggle", params)


@app.post("/api/v1/ui/slider", tags=["ui"])
async def ui_add_slider(body: UIAddSliderBody):
    return await call_unity_async("spz.ui.add_slider", {
        "addon_id": body.addon_id,
        "panel_id": body.panel_id,
        "label": body.label,
        "min": body.min,
        "max": body.max,
        "default": body.default,
    })


@app.post("/api/v1/ui/text_input", tags=["ui"])
async def ui_add_text_input(body: UIAddTextInputBody):
    return await call_unity_async("spz.ui.add_text_input", {
        "addon_id": body.addon_id,
        "panel_id": body.panel_id,
        "label": body.label,
        "default": body.default,
    })


@app.post("/api/v1/ui/dropdown", tags=["ui"])
async def ui_add_dropdown(body: UIAddDropdownBody):
    return await call_unity_async("spz.ui.add_dropdown", {
        "addon_id": body.addon_id,
        "panel_id": body.panel_id,
        "label": body.label,
        "options": body.options,
        "default": body.default,
    })


@app.get("/api/v1/ui/value/{element_id}", tags=["ui"])
async def ui_get_value(element_id: str):
    return await call_unity_async("spz.ui.get_value", {"element_id": element_id})


@app.post("/api/v1/ui/value", tags=["ui"])
async def ui_set_value(body: UISetValueBody):
    return await call_unity_async("spz.ui.set_value", {
        "element_id": body.element_id,
        "value": body.value,
    })


@app.get("/api/v1/ui/theme", tags=["ui"])
async def ui_get_theme():
    """Return the active theme plus schema, surfaces, and composition metadata."""
    return await call_unity_async("spz.ui.get_theme", {})


@app.get("/api/v1/ui/themes", tags=["ui"])
async def ui_list_themes():
    """List builtin and registered theme presets."""
    return await call_unity_async("spz.ui.list_themes", {})


@app.post("/api/v1/ui/themes/register", tags=["ui"])
async def ui_register_theme(body: UIRegisterThemeBody):
    """Register or atomically replace a theme preset."""
    params = {
        "theme_id": body.theme_id,
        "tokens": body.tokens,
    }
    if body.label is not None:
        params["label"] = body.label
    if body.owner is not None:
        params["owner"] = body.owner
    return await call_unity_async("spz.ui.register_theme", params)


@app.post("/api/v1/ui/themes/unregister", tags=["ui"])
async def ui_unregister_theme(body: UIUnregisterThemeBody):
    """Remove a registered preset without changing the active palette."""
    return await call_unity_async("spz.ui.unregister_theme", {
        "theme_id": body.theme_id,
    })


@app.post("/api/v1/ui/theme", tags=["ui"])
async def ui_apply_theme(body: UIApplyThemeBody):
    """Apply tokens or a registered preset using replace/patch semantics."""
    params = {"theme_id": body.theme_id}
    if body.tokens is not None:
        params["tokens"] = body.tokens
    if body.mode is not None:
        params["mode"] = body.mode
    return await call_unity_async("spz.ui.apply_theme", params)


@app.post("/api/v1/ui/theme/reset", tags=["ui"])
async def ui_reset_theme():
    """Restore the built-in StableProjectorz runtime UI color tokens."""
    return await call_unity_async("spz.ui.reset_theme", {})


@app.get("/api/v1/ui/line_icons", tags=["ui"])
async def ui_list_line_icons():
    """List built-in StudioLineIcon names (icon pack v1, rpc 1.15+)."""
    return await call_unity_async("spz.ui.list_line_icons", {})


@app.post("/api/v1/ui/line_icon", tags=["ui"])
async def ui_set_line_icon(body: UISetLineIconBody):
    """Set a CommandRibbon strip tab line glyph by tab name substring."""
    return await call_unity_async("spz.ui.set_line_icon", {
        "tab": body.tab,
        "icon": body.icon,
    })

# ============================================
# Addon loading (Unity calls this when user enables an addon or at startup)
# ============================================

@app.get("/ready")
async def ready():
    """
    Forge-style split: HTTP is always up when this returns; 'ready' means Python linked to Unity TCP (5555).
    Unity polls this like SD readiness — distinguish api_up vs unity_linked.
    """
    if _connection_ready_callback is None:
        return {
            "ready": False,
            "api_up": True,
            "unity_linked": False,
            "reason": "no callback",
        }
    try:
        ready_val = await _connection_ready_async()
        return {
            "ready": ready_val,
            "api_up": True,
            "unity_linked": ready_val,
        }
    except Exception as e:
        return {
            "ready": False,
            "api_up": True,
            "unity_linked": False,
            "reason": str(e),
        }


@app.post("/load_addon")
async def load_addon(req: LoadAddonRequest):
    """Load a single addon by id. Called by Unity when an addon is enabled."""
    if _load_addon_callback is None:
        raise HTTPException(status_code=503, detail="Addon loader not registered")
    # Keep readiness probe as advisory only.
    # Strict gating can strand add-on load when the probe is transiently false even though
    # the callback path would still work (or recover on immediate retry).
    if _connection_ready_callback is not None:
        try:
            _ = await _connection_ready_async()
        except Exception:
            pass
    try:
        ok = await asyncio.to_thread(_load_addon_callback, req.addon_id)
        return {"success": ok, "addon_id": req.addon_id}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/unload_addon")
async def unload_addon(req: LoadAddonRequest):
    """Unload a single addon by id. Called by Unity when an addon is disabled."""
    if _unload_addon_callback is None:
        raise HTTPException(status_code=503, detail="Addon unloader not registered")
    try:
        ok = await asyncio.to_thread(_unload_addon_callback, req.addon_id)
        return {"success": ok, "addon_id": req.addon_id}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/invoke_callback")
async def invoke_callback(req: InvokeCallbackRequest):
    """Invoke an addon function by name. Called by Unity when user clicks an addon panel button."""
    if _invoke_callback is None:
        raise HTTPException(status_code=503, detail="Invoke callback not registered")
    # Do not hard-gate callbacks on connection-ready probes.
    # Button clicks originate from Unity itself, and callbacks may not need an immediate
    # round-trip probe before entering Python. A transient probe failure here makes
    # command-ribbon buttons appear "dead" even when invocation would otherwise work.
    if _connection_ready_callback is not None:
        try:
            _ = await _connection_ready_async()
        except Exception:
            pass
    try:
        ok = await asyncio.to_thread(_invoke_callback, req.addon_id, req.callback)
        return {"success": ok, "addon_id": req.addon_id, "callback": req.callback}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/notify_value_change")
async def notify_value_change(req: NotifyValueChangeRequest):
    """Forward a Unity widget value change to the loaded add-on's optional on_value_change hook."""
    if _notify_value_change_callback is None:
        raise HTTPException(status_code=503, detail="Notify value-change callback not registered")
    if _connection_ready_callback is not None:
        try:
            _ = await _connection_ready_async()
        except Exception:
            pass
    try:
        ok = await asyncio.to_thread(
            _notify_value_change_callback,
            req.addon_id,
            req.element_id,
            req.element_type,
            req.value,
        )
        return {
            "success": ok,
            "addon_id": req.addon_id,
            "element_id": req.element_id,
            "element_type": req.element_type,
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

# ============================================
# Health Check
# ============================================

@app.get("/")
async def root():
    """Root endpoint — same role as hitting Forge root: confirms local HTTP server is up."""
    unity_ok = False
    if _connection_ready_callback is not None:
        try:
            unity_ok = await _connection_ready_async()
        except Exception:
            unity_ok = False
    return {
        "name": "StableProjectorz Add-on API",
        "version": "1.0.0",
        "docs": "/docs",
        "status": "running",
        "unity_linked": unity_ok,
        "meta_http": "GET /api/v1/meta — method catalog; GET /api/v1/context — live snapshot → spz.cmd.*",
        "projection_http": "/api/v1/projection/cameras/* — count, get/set position & rotation → spz.cmd.*",
        "meshes_http": "/api/v1/meshes/* — selection, transforms, bounds, visibility, name, batch pos/rot/scale → spz.cmd.*",
        "scene_http": "/api/v1/scene/* — info, selected_bounds, select/deselect all",
        "gen3d_http": "/api/v1/gen3d/* — connected, ready, in_progress, trigger",
        "export_http": "/api/v1/export/* — 3d_with_textures, 3d_to_path, projection_textures, view_textures; "
        "POST /api/v1/meshes/import for DCC file path import",
        "paint_brush_http": "/api/v1/paint/* (tag 'paint') — brush settings, layer stack, set size/spacing/angle/roundness/opacity/stamp, active layer → spz.cmd.*",
        "sd_forge_http": "/api/v1/sd/* (tag 'sd') — prompts, generate, workflow mode, generation options (denoise/blur/toggles), ControlNet, skybox → spz.cmd.*",
        "add_on_ui_http": "/api/v1/ui/* (OpenAPI tag 'ui') — panel, button, slider, text_input, dropdown, value get/set → spz.ui.*",
        "chrome_http": "/api/v1/chrome/* — ribbon, cursor, ui-scale, named ui-targets, status text, EventSystem → spz.cmd.*",
        "note": "Like Forge: this URL means FastAPI is listening; unity_linked follows once Python connects to Unity TCP.",
    }

@app.get("/health")
async def health():
    """Health check endpoint"""
    if _api is None:
        return {"status": "disconnected", "unity": False}
    try:
        # Try a simple call to Unity
        await call_unity_async("spz.cmd.get_total_mesh_count", {})
        return {"status": "connected", "unity": True}
    except Exception:
        return {"status": "disconnected", "unity": False}

def start_server(host: str = "127.0.0.1", port: int = 5557):
    """Start the FastAPI server. Binds to 127.0.0.1 by default (Unity on same machine calls /load_addon, /ready, etc.)."""
    print(f"[HTTP Server] Starting FastAPI server on http://{host}:{port}")
    print(f"[HTTP Server] API docs: http://{host}:{port}/docs")
    print(f"[HTTP Server] Running on local URL: http://{host}:{port}  (Forge-style: HTTP up first; Unity links via /ready)")
    uvicorn.run(app, host=host, port=port, log_level="info")
