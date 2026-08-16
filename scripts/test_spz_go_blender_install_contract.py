"""Contracts for SPZ GO Blender bridge ship + auto-install UI."""
from pathlib import Path
import re
import tempfile
import unittest
import importlib.util

ROOT = Path(__file__).resolve().parents[1]
ADDON = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "__init__.py"
BRIDGE = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "BlenderBridge"


def _load_addon_helpers():
	"""Load parse_bl_info_version / bridge_ship_dir without requiring spz."""
	spec = importlib.util.spec_from_file_location("spz_go_addon_under_test", ADDON)
	mod = importlib.util.module_from_spec(spec)
	# Avoid executing full register path side effects beyond imports — module load is OK.
	spec.loader.exec_module(mod)
	return mod


class SpzGoBlenderInstallContractTests(unittest.TestCase):
	def test_ship_bridge_contains_required_files(self):
		self.assertTrue(BRIDGE.is_dir(), "BlenderBridge ship folder must exist")
		for name in ("__init__.py", "spz_http.py", "blender_manifest.toml", "install_into_blender.py"):
			self.assertTrue((BRIDGE / name).is_file(), f"missing ship file {name}")

	def test_register_has_install_button_and_auto_call(self):
		src = ADDON.read_text(encoding="utf-8")
		self.assertIn('add_button("Install into Blender", "do_install_blender_addon_force")', src)
		self.assertIn("do_install_blender_addon(force=False, report_status=False)", src)
		self.assertIn("threading.Thread", src)
		self.assertIn("SpzGoBlenderAutoInstall", src)
		self.assertIn("def do_install_blender_addon", src)
		self.assertIn("report_status", src)
		self.assertIn("def bridge_ship_dir", src)

	def test_parse_bl_info_version(self):
		mod = _load_addon_helpers()
		ver = mod.parse_bl_info_version(str(BRIDGE / "__init__.py"))
		self.assertIsNotNone(ver)
		self.assertEqual(len(ver), 3)
		# Pinning the literal here just rots on every bridge bump. What actually matters is that
		# the parsed version tracks bl_info, since auto-install compares it to decide whether an
		# already-installed bridge must be refreshed.
		src = (BRIDGE / "__init__.py").read_text(encoding="utf-8")
		self.assertIn('"version": (%d, %d, %d)' % ver, src)
		self.assertGreaterEqual(ver, (0, 4, 0),
			"bridge behaviour changed; bump bl_info or installed copies never update")

	def test_manifest_version_matches_bl_info(self):
		mod = _load_addon_helpers()
		ver = mod.parse_bl_info_version(str(BRIDGE / "__init__.py"))
		manifest = (BRIDGE / "blender_manifest.toml").read_text(encoding="utf-8")
		self.assertIn('version = "%d.%d.%d"' % ver, manifest,
			"extension manifest and bl_info must agree or Blender 4.2+ reports a stale version")

	def test_parse_bl_info_version_missing_file(self):
		mod = _load_addon_helpers()
		self.assertIsNone(mod.parse_bl_info_version(str(BRIDGE / "no_such_init.py")))

	def test_install_script_markers(self):
		script = (BRIDGE / "install_into_blender.py").read_text(encoding="utf-8")
		self.assertIn("SPZ_GO_INSTALL_OK", script)
		self.assertIn("SPZ_GO_INSTALL_SKIP", script)
		self.assertIn("SPZ_GO_INSTALL_FAIL", script)
		self.assertIn("spz_blender_bridge", script)
		self.assertIn("save_userpref", script)
		self.assertIn("up-to-date but could not enable", script)
		# SKIP path must check enable result (not ignore _enable return).
		self.assertIn("if not _enable(MODULE_NAME):", script)


if __name__ == "__main__":
	unittest.main()
