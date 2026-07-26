"""Contract: SPZ GO in-app panel uses short labels and demoted Blender row."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
ADDON = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "__init__.py"


class SpzGoUiCopyContractTests(unittest.TestCase):
	def test_register_uses_short_primary_labels(self):
		src = ADDON.read_text(encoding="utf-8")
		self.assertIn('add_text_input("Import path"', src)
		self.assertIn('add_text_input("Export path"', src)
		self.assertIn('add_text_input("Blender.exe (optional)"', src)
		self.assertIn('add_button("Autofill paths"', src)
		self.assertIn('add_button("Refresh Blender"', src)
		self.assertIn('add_button("Install into Blender"', src)
		# Primary actions before helpers
		i_imp = src.index('add_button("Import"')
		i_exp = src.index('add_button("Export"')
		i_inst = src.index('add_button("Install into Blender"')
		i_auto = src.index('add_button("Autofill paths"')
		self.assertLess(i_imp, i_auto)
		self.assertLess(i_exp, i_inst)
		self.assertLess(i_inst, i_auto)
		# Paths before optional Blender
		i_path_imp = src.index('add_text_input("Import path"')
		i_blender = src.index('add_text_input("Blender.exe (optional)"')
		self.assertLess(i_path_imp, i_blender)


if __name__ == "__main__":
	unittest.main()
