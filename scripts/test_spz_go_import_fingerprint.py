"""Pure tests for SPZ GO import wait fingerprint (no bpy)."""
import os
import tempfile
import time
import unittest
from pathlib import Path
import importlib.util

ROOT = Path(__file__).resolve().parents[1]


def _load_fingerprint_helpers():
	"""Load _file_fingerprint from Blender bridge without importing bpy-dependent module top-level.

	We exec only the helper by copying the function source into a tiny module stub.
	"""
	src = (ROOT / "External" / "Blender_SpzBridge" / "__init__.py").read_text(encoding="utf-8")
	# Extract _file_fingerprint function body via a minimal sandbox.
	start = src.index("def _file_fingerprint(path: str):")
	end = src.index("\ndef _go_import_timer():", start)
	ns = {"os": os}
	exec(src[start:end], ns)
	return ns["_file_fingerprint"]


class SpzGoImportFingerprintTests(unittest.TestCase):
	def test_fingerprint_changes_when_file_rewritten(self):
		fp_fn = _load_fingerprint_helpers()
		with tempfile.TemporaryDirectory() as td:
			path = os.path.join(td, "from_spz.fbx")
			with open(path, "wb") as f:
				f.write(b"x" * 64)
			prior = fp_fn(path)
			self.assertIsNotNone(prior)
			time.sleep(0.02)
			with open(path, "wb") as f:
				f.write(b"y" * 80)
			after = fp_fn(path)
			self.assertIsNotNone(after)
			self.assertNotEqual(prior, after)

	def test_missing_file_fingerprint_is_none(self):
		fp_fn = _load_fingerprint_helpers()
		self.assertIsNone(fp_fn(os.path.join(tempfile.gettempdir(), "spz_go_missing_no_such.fbx")))


if __name__ == "__main__":
	unittest.main()
