"""Pure checks for SPZ GO Blender bridge contracts (no bpy required)."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
BRIDGE = ROOT / "External" / "Blender_SpzBridge" / "__init__.py"


class SpzGoApplyMapsContractTests(unittest.TestCase):
	def test_apply_maps_only_reports_failure_when_auto_apply_fails(self):
		src = BRIDGE.read_text(encoding="utf-8")
		self.assertIn("def _auto_apply_exchange_texture_after_import(fbx_path: str) -> bool:", src)
		# Operator must not claim INFO success unless auto-apply returned true.
		self.assertRegex(
			src,
			re.compile(
				r"if not _auto_apply_exchange_texture_after_import\(fbx\):.*?return \{\"CANCELLED\"\}",
				re.S,
			),
		)
		self.assertIn('self.report({"INFO"}, "SPZ maps applied to selected mesh(es).")', src)


if __name__ == "__main__":
	unittest.main()
