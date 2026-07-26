"""Contract checks for in-app StableProjectorzGO add-on."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
ADDON = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "__init__.py"


class SpzGoInAppImportContractTests(unittest.TestCase):
	def test_import_aborts_when_file_missing(self):
		src = ADDON.read_text(encoding="utf-8")
		self.assertIn("def do_import_from_path():", src)
		self.assertRegex(
			src,
			re.compile(
				r"if not os\.path\.isfile\(path\):.*?import aborted — file not found",
				re.S,
			),
		)
		self.assertIn('show_status_text("Import: file not found"', src)


if __name__ == "__main__":
	unittest.main()
