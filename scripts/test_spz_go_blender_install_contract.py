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
		self.assertIn("do_install_blender_addon(force=False)", src)
		self.assertIn("threading.Thread", src)
		self.assertIn("SpzGoBlenderAutoInstall", src)
		self.assertIn("def do_install_blender_addon", src)
		self.assertIn("def bridge_ship_dir", src)

	def test_parse_bl_info_version(self):
		mod = _load_addon_helpers()
		ver = mod.parse_bl_info_version(str(BRIDGE / "__init__.py"))
		self.assertEqual(ver, (0, 2, 0))

	def test_parse_bl_info_version_missing_file(self):
		mod = _load_addon_helpers()
		self.assertIsNone(mod.parse_bl_info_version(str(BRIDGE / "no_such_init.py")))

	def test_install_script_markers(self):
		script = (BRIDGE / "install_into_blender.py").read_text(encoding="utf-8")
		self.assertIn("SPZ_GO_INSTALL_OK", script)
		self.assertIn("SPZ_GO_INSTALL_SKIP", script)
		self.assertIn("SPZ_GO_INSTALL_FAIL", script)
		self.assertIn("spz_blender_bridge", script)


if __name__ == "__main__":
	unittest.main()
