"""Contract: SPZ Export stamps .spz_go_ready; Blender auto-watches + FBX scale/axis lock both ways."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SHIP = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "BlenderBridge" / "__init__.py"
EXTERNAL = ROOT / "External" / "Blender_SpzBridge" / "__init__.py"
SAVE_MGR = ROOT / "Assets" / "_gm" / "Features" / "Save Load Import Export" / "Save_MGR.cs"


class SpzGoBidirectionalAutoimportContractTests(unittest.TestCase):
	def test_ship_and_external_bridge_are_synced(self):
		self.assertTrue(SHIP.is_file(), f"missing ship bridge: {SHIP}")
		self.assertTrue(EXTERNAL.is_file(), f"missing External bridge: {EXTERNAL}")
		self.assertEqual(
			SHIP.read_bytes(),
			EXTERNAL.read_bytes(),
			"StreamingAssets BlenderBridge/__init__.py must match External/Blender_SpzBridge/__init__.py",
		)

	def test_blender_auto_import_watch_and_scale_lock(self):
		src = SHIP.read_text(encoding="utf-8")
		self.assertIn("auto_import_from_spz", src)
		self.assertIn("_exchange_watch_timer", src)
		self.assertIn(".spz_go_ready", src)
		self.assertIn('axis_forward=_FBX_AXIS_FORWARD', src)
		self.assertIn('axis_up=_FBX_AXIS_UP', src)
		self.assertIn('apply_scale_options="FBX_SCALE_ALL"', src)
		self.assertIn("bpy.app.timers.register(_exchange_watch_timer", src)
		self.assertIn("_seed_watch_fingerprint", src)
		self.assertIn("_apply_spz_export_scale_litmus", src)
		self.assertIn("scale_undid_fit", src)

	def test_unity_writes_ready_stamp_after_textures(self):
		src = SAVE_MGR.read_text(encoding="utf-8")
		self.assertIn("TryWriteSpzGoExchangeReadyStamp", src)
		self.assertIn(".spz_go_ready", src)
		self.assertIn("scale_undid_fit=1", src)
		# Stamp only after texture OnComplete, not immediately after mesh write.
		idx_complete = src.find("void OnComplete()")
		idx_stamp = src.find("TryWriteSpzGoExchangeReadyStamp( meshPathForStamp )")
		self.assertGreater(idx_complete, 0)
		self.assertGreater(idx_stamp, idx_complete)

	def test_unity_fbx_export_undoes_fit_for_cube_litmus(self):
		helper = ROOT / "Assets" / "_gm" / "Features" / "3D Models" / "ModelsHandler3D_ImportHelper.cs"
		container = ROOT / "Assets" / "_gm" / "Features" / "3D Models" / "Objs3D_Container.cs"
		h = helper.read_text(encoding="utf-8")
		c = container.read_text(encoding="utf-8")
		self.assertIn("TryBeginFbxExportAuthoringScale", h)
		self.assertIn("EndFbxExportAuthoringScale", h)
		self.assertIn("BlenderDefaultCubeEdgeMeters", c)
		self.assertIn("SpzFitTargetMaxDimension", c)


if __name__ == "__main__":
	unittest.main()
