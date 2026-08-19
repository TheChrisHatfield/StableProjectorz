"""Static wiring checks for MultiviewManagerSPZ (run: python deep_wiring_checks.py)."""

from __future__ import annotations

import os
import sys

_ROOT = os.path.dirname(os.path.abspath(__file__))
_INIT = os.path.join(_ROOT, "__init__.py")
_SPZ = os.path.normpath(os.path.join(_ROOT, "..", "..", "AddonSystem", "spz.py"))
_SOCKET = os.path.normpath(
    os.path.join(_ROOT, "..", "..", "..", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs")
)
_FAST = os.path.normpath(
    os.path.join(_ROOT, "..", "..", "..", "_gm", "Features", "AddonSystem", "FastPath_API.cs")
)


def _read(path: str) -> str:
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def check(ok: bool, msg: str) -> None:
    if not ok:
        raise SystemExit(f"FAIL: {msg}")
    print(f"OK: {msg}")


def main() -> None:
    init = _read(_INIT)
    spz = _read(_SPZ)
    socket = _read(_SOCKET)
    fast = _read(_FAST)

    check("MultiviewManagerSPZ" in init, "addon id matches folder")
    check("create_panel(ADDON_ID" in init, "register creates ribbon panel")
    check("_preset_name_from_panel" in init, "preset load resolves dropdown index")
    check("_capture_pre_isolate_if_multiview" in init, "isolate preserves multiview snapshot")
    check("_isolate_view_at" in init, "quick-view buttons use direct slot index")
    check("is not enabled" in init or "is not active" in init, "isolate guards inactive slots")
    check("type(_panel)(_panel._client, iso_fold" in init, "foldout widgets use content panel id")

    for rpc in (
        "get_view_camera_povs",
        "isolate_view_camera",
        "restore_view_camera_povs",
        "apply_view_camera_slot_pov",
    ):
        check(rpc in spz, f"spz.py exposes {rpc}")
        check(f"spz.cmd.{rpc}" in socket, f"Addon_SocketServer handles {rpc}")

    check("GetViewCameraPovsJson" in fast, "FastPath get POV snapshot")
    check("RestoreViewCameraPovsFromJson" in fast, "FastPath restore POV snapshot")
    check("TryIsolateViewCamera" in _read(
        os.path.normpath(os.path.join(_ROOT, "..", "..", "..", "_gm", "Features", "Camera", "UserCameras_MGR.cs"))
    ), "UserCameras_MGR isolate helper")

    print("All MultiviewManagerSPZ wiring checks passed.")


if __name__ == "__main__":
    main()
