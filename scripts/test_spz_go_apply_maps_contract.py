"""Pure checks for SPZ GO Blender bridge contracts (no bpy required)."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
BRIDGE = ROOT / "External" / "Blender_SpzBridge" / "__init__.py"
HTTP = ROOT / "External" / "Blender_SpzBridge" / "spz_http.py"


class SpzGoApplyMapsContractTests(unittest.TestCase):
	def test_apply_maps_only_reports_failure_when_auto_apply_fails(self):
		src = BRIDGE.read_text(encoding="utf-8")
		self.assertIn("def _auto_apply_exchange_texture_after_import(fbx_path: str) -> bool:", src)
		# Operator must not claim INFO success unless auto-apply returned true.
		self.assertIn("applied = False", src)
		self.assertIn("if not applied:", src)
		self.assertIn('return {"CANCELLED"}', src)
		self.assertIn('self.report({"INFO"}, "SPZ maps applied to selected mesh(es).")', src)

	def test_go_import_imports_immediately_after_http_ok(self):
		src = BRIDGE.read_text(encoding="utf-8")
		self.assertIn("_try_import_exchange_fbx(fbx)", src)
		self.assertIn("_GO_IMPORT_MAX_TICKS", src)
		self.assertIn("http_ok", src)

	def test_export_http_timeout_matches_long_texture_write(self):
		http = HTTP.read_text(encoding="utf-8")
		self.assertIn("timeout_s=300.0", http)


if __name__ == "__main__":
	unittest.main()
