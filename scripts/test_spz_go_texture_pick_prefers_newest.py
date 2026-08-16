"""Repeat exports must not re-apply a texture left over from an earlier one.

Older SPZ builds uniquified the texture written beside the exchange FBX, so a folder can hold
"from_spz.png" alongside "from_spz 2.png". Ranking ties by name puts the space before the dot and
hands the win to the older file, which is what made a re-export look like it had not taken effect.
"""

import os
import sys
import time
import types
import unittest
from pathlib import Path
from typing import Optional

REPO = Path(__file__).resolve().parents[1]
BRIDGE = REPO / "External" / "Blender_SpzBridge"


def _load_picker():
    """Import _find_best_exchange_texture_for_fbx without pulling in bpy."""
    src = (BRIDGE / "__init__.py").read_text(encoding="utf-8")
    start = src.index("def _find_best_exchange_texture_for_fbx")
    end = src.index("def _ensure_image_to_principled_basecolor")
    mod = types.ModuleType("spz_picker")
    mod.__dict__.update({"os": os, "Path": Path, "Optional": Optional})
    exec(compile(src[start:end], "picker", "exec"), mod.__dict__)
    return mod._find_best_exchange_texture_for_fbx


class TexturePickPrefersNewest(unittest.TestCase):
    def setUp(self):
        import tempfile

        self.tmp = tempfile.TemporaryDirectory()
        self.folder = Path(self.tmp.name)
        self.fbx = self.folder / "from_spz.fbx"
        self.fbx.write_bytes(b"")
        self.pick = _load_picker()

    def tearDown(self):
        self.tmp.cleanup()

    def _write(self, name, age_seconds):
        p = self.folder / name
        p.write_bytes(b"x")
        stamp = time.time() - age_seconds
        os.utime(p, (stamp, stamp))
        return p

    def test_newest_wins_over_alphabetical_leftover(self):
        # "from_spz 2.png" sorts before "from_spz.png" by name, so the name tiebreak used to win.
        self._write("from_spz 2.png", age_seconds=600)
        fresh = self._write("from_spz.png", age_seconds=1)
        self.assertEqual(os.path.normpath(self.pick(str(self.fbx))), os.path.normpath(str(fresh)))

    def test_score_still_beats_recency(self):
        # An unrelated but newer image must not outrank a name match.
        self._write("unrelated.png", age_seconds=0)
        match = self._write("from_spz.png", age_seconds=900)
        self.assertEqual(os.path.normpath(self.pick(str(self.fbx))), os.path.normpath(str(match)))

    def test_ao_map_is_still_deprioritised(self):
        self._write("from_spz_AO.png", age_seconds=0)
        albedo = self._write("from_spz.png", age_seconds=300)
        self.assertEqual(os.path.normpath(self.pick(str(self.fbx))), os.path.normpath(str(albedo)))


if __name__ == "__main__":
    sys.exit(0 if unittest.main(exit=False).result.wasSuccessful() else 1)
