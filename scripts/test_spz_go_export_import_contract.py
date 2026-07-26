"""Contract: Blender Export to SPZ must not finish when Unity import fails."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
BRIDGE = ROOT / "External" / "Blender_SpzBridge" / "__init__.py"


class SpzGoExportImportContractTests(unittest.TestCase):
	def test_go_export_cancels_when_spz_import_fails(self):
		src = BRIDGE.read_text(encoding="utf-8")
		# Locate go_export operator body after bl_idname.
		m = re.search(
			r'bl_idname = "spz\.go_export".*?def execute\(self, context\):(.*?)(?=\nclass |\ndef )',
			src,
			re.S,
		)
		self.assertIsNotNone(m, "spz.go_export execute body not found")
		body = m.group(1)
		self.assertIn("post_import_3d_model", body)
		self.assertIn('return {"CANCELLED"}', body)
		self.assertRegex(
			body,
			re.compile(
				r"if ok:.*?return \{\"FINISHED\"\}.*?SPZ import failed.*?return \{\"CANCELLED\"\}",
				re.S,
			),
		)


if __name__ == "__main__":
	unittest.main()
