"""Autofill must not claim both paths empty when only one is missing."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
ADDON = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "__init__.py"


class SpzGoAutofillContractTests(unittest.TestCase):
	def test_autofill_distinguishes_empty_vs_partial(self):
		src = ADDON.read_text(encoding="utf-8")
		body_m = re.search(r"def do_autofill_mesh_paths\(\):(.*?)(?=\ndef )", src, re.S)
		self.assertIsNotNone(body_m)
		body = body_m.group(1)
		self.assertIn("if not ip and not ep:", body)
		self.assertIn("elif not ip or not ep:", body)
		self.assertIn("autofill partial", body)
		self.assertNotIn("if not (ip and ep):", body)


if __name__ == "__main__":
	unittest.main()
