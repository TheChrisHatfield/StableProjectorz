# SPDX-License-Identifier: MIT
"""
Install/update SPZ GO Blender bridge into the user add-ons folder.
Invoked by StableProjectorz:
  blender --background --python install_into_blender.py -- --src <BlenderBridgeDir> [--force]

Stdout markers (parsed by SPZ):
  SPZ_GO_INSTALL_OK
  SPZ_GO_INSTALL_SKIP: <reason>
  SPZ_GO_INSTALL_FAIL: <reason>
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import sys


MODULE_NAME = "spz_blender_bridge"
SHIP_FILES = ("__init__.py", "spz_http.py", "mesh_stream.py", "blender_manifest.toml")


def _parse_bl_info_version(init_path: str):
	try:
		text = open(init_path, "r", encoding="utf-8", errors="replace").read()
	except OSError:
		return None
	m = re.search(r'"version"\s*:\s*\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)\)', text)
	if not m:
		return None
	return (int(m.group(1)), int(m.group(2)), int(m.group(3)))


def main(argv=None) -> int:
	# When Blender runs --python, argv is [script, ...] or [script, --, ...].
	raw = list(sys.argv if argv is None else argv)
	if "--" in raw:
		raw = raw[raw.index("--") + 1 :]
	elif raw and raw[0].endswith(".py"):
		raw = raw[1:]

	ap = argparse.ArgumentParser(description="Install SPZ GO Blender bridge")
	ap.add_argument("--src", required=True, help="Path to shipped BlenderBridge folder")
	ap.add_argument("--force", action="store_true", help="Reinstall even when version matches")
	args = ap.parse_args(raw)

	src = os.path.abspath(args.src)
	if not os.path.isdir(src):
		print("SPZ_GO_INSTALL_FAIL: ship dir missing: " + src)
		return 1
	src_init = os.path.join(src, "__init__.py")
	if not os.path.isfile(src_init):
		print("SPZ_GO_INSTALL_FAIL: ship __init__.py missing")
		return 1

	try:
		import bpy  # noqa: F401 — must run inside Blender
	except ImportError:
		print("SPZ_GO_INSTALL_FAIL: bpy not available (run via blender --python)")
		return 1

	addons_root = bpy.utils.user_resource("SCRIPTS", path="addons")
	if not addons_root:
		print("SPZ_GO_INSTALL_FAIL: could not resolve user scripts/addons")
		return 1
	os.makedirs(addons_root, exist_ok=True)
	dest = os.path.join(addons_root, MODULE_NAME)
	dest_init = os.path.join(dest, "__init__.py")

	ship_ver = _parse_bl_info_version(src_init)
	inst_ver = _parse_bl_info_version(dest_init) if os.path.isfile(dest_init) else None
	if (
		not args.force
		and ship_ver is not None
		and inst_ver is not None
		and ship_ver == inst_ver
		and os.path.isfile(os.path.join(dest, "spz_http.py"))
		and os.path.isfile(os.path.join(dest, "mesh_stream.py"))
	):
		# Still ensure enabled — SKIP must not hide a failed enable.
		if not _enable(MODULE_NAME):
			print("SPZ_GO_INSTALL_FAIL: up-to-date but could not enable " + MODULE_NAME)
			return 1
		print("SPZ_GO_INSTALL_SKIP: already up-to-date " + ".".join(str(x) for x in ship_ver))
		return 0

	try:
		if os.path.isdir(dest):
			shutil.rmtree(dest)
		os.makedirs(dest, exist_ok=True)
		for name in SHIP_FILES:
			s = os.path.join(src, name)
			if not os.path.isfile(s):
				print("SPZ_GO_INSTALL_FAIL: missing ship file " + name)
				return 1
			shutil.copy2(s, os.path.join(dest, name))
	except OSError as e:
		print("SPZ_GO_INSTALL_FAIL: copy failed: " + str(e))
		return 1

	if not _enable(MODULE_NAME):
		print("SPZ_GO_INSTALL_FAIL: copied but could not enable " + MODULE_NAME)
		return 1

	ver_s = ".".join(str(x) for x in ship_ver) if ship_ver else "?"
	print("SPZ_GO_INSTALL_OK: installed " + MODULE_NAME + " " + ver_s + " -> " + dest)
	return 0


def _enable(module: str) -> bool:
	import bpy

	try:
		bpy.ops.preferences.addon_refresh()
	except Exception:
		pass
	# bpy ops return {'CANCELLED'} without raising — never trust a bare call.
	try:
		ret = bpy.ops.preferences.addon_enable(module=module)
		if ret is None or "FINISHED" not in ret:
			print("SPZ_GO_INSTALL_NOTE: addon_enable returned " + repr(ret))
	except Exception as e:
		# Already enabled or alternate key — verified via prefs membership below.
		print("SPZ_GO_INSTALL_NOTE: addon_enable: " + str(e))
	enabled = False
	try:
		enabled = module in bpy.context.preferences.addons
	except Exception as e:
		print("SPZ_GO_INSTALL_NOTE: prefs check: " + str(e))
	if not enabled:
		return False
	# Background installs must write prefs or enable is lost on next Blender launch.
	try:
		ret = bpy.ops.wm.save_userpref()
		if ret is None or "FINISHED" not in ret:
			print("SPZ_GO_INSTALL_FAIL: save_userpref returned " + repr(ret))
			return False
	except Exception as e:
		print("SPZ_GO_INSTALL_FAIL: save_userpref: " + str(e))
		return False
	return True


if __name__ == "__main__":
	sys.exit(main())
