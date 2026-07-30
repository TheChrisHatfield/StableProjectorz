"""Pure checks for SPZ GO Blender bridge contracts (no bpy required)."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
BRIDGE = ROOT / "External" / "Blender_SpzBridge" / "__init__.py"
HTTP = ROOT / "External" / "Blender_SpzBridge" / "spz_http.py"


class SpzGoApplyMapsContractTests(unittest.TestCase):
	def test_apply_maps_only_requires_mesh_before_poll(self):
		src = BRIDGE.read_text(encoding="utf-8")
		self.assertIn("def _auto_apply_exchange_texture_after_import(fbx_path: str) -> bool:", src)
		self.assertIn("def _mesh_targets_for_maps", src)
		self.assertIn("Select a mesh object before Apply SPZ maps only", src)
		self.assertIn("_find_best_exchange_texture_for_fbx(fbx)", src)
		self.assertIn('self.report({"INFO"}, "SPZ maps applied to selected mesh(es).")', src)

	def test_go_import_imports_immediately_after_http_ok(self):
		src = BRIDGE.read_text(encoding="utf-8")
		self.assertIn("_try_import_exchange_fbx(fbx)", src)
		self.assertIn("_GO_IMPORT_MAX_TICKS", src)
		self.assertIn("http_ok", src)
		self.assertIn('"FINISHED" not in ret', src)

	def test_export_http_timeout_matches_long_texture_write(self):
		http = HTTP.read_text(encoding="utf-8")
		self.assertIn("timeout_s=300.0", http)
		self.assertIn("/api/v1/meshes/import", http)


if __name__ == "__main__":
	unittest.main()
