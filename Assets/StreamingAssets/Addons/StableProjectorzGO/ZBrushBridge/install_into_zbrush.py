# SPDX-License-Identifier: MIT
"""
Copy the SPZ GO ZBrush bridge into a user-writable ZBrush scripts folder.

Standalone (does not launch ZBrush): StableProjectorz normally copies the files itself, but this
script mirrors that so the install is reproducible/manual too:

  python install_into_zbrush.py --src <ZBrushBridgeDir> [--dest <ZBrushUserScriptsDir>]

Stdout markers (parsed by SPZ if invoked):
  SPZ_GO_INSTALL_OK: <dest>
  SPZ_GO_INSTALL_FAIL: <reason>

Never targets Program Files — ZBrush keeps user scripts under the user profile / Documents.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys

SHIP_FILES = ("spz_zbrush_bridge.py", "spz_http.py")
DEST_SUBDIR = "SpzGoBridge"


def find_zbrush_user_scripts() -> str:
    """User-writable ZBrush data folder (Windows). '' when unknown.

    ZBrush 2026 keeps its user data under Public Documents (e.g.
    ``C:\\Users\\Public\\Documents\\ZBrushData2026``), not the personal Documents folder, so search
    there first. We install into a ``SpzGoBridge`` subfolder of the ZBrushData root (writable without
    elevation); the user loads the script once via ZPlugin/ZScript → Python → Load.
    """
    roots = []
    public_docs = os.path.join(os.environ.get("PUBLIC", r"C:\Users\Public"), "Documents")
    home = os.path.expanduser("~")
    for base in (public_docs, os.path.join(home, "Documents"), home):
        if not os.path.isdir(base):
            continue
        try:
            for name in os.listdir(base):
                low = name.lower()
                if low.startswith("zbrushdata") or "zbrush" in low or low.startswith("maxon zbrush"):
                    roots.append(os.path.join(base, name))
        except OSError:
            pass
    # Newest ZBrushData* wins (handles multiple installed years).
    roots.sort(key=lambda p: os.path.getmtime(p) if os.path.exists(p) else 0, reverse=True)
    return roots[0] if roots else ""


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Install SPZ GO ZBrush bridge")
    ap.add_argument("--src", required=True, help="Path to shipped ZBrushBridge folder")
    ap.add_argument("--dest", default="", help="ZBrush user scripts dir (auto-resolved if omitted)")
    args = ap.parse_args(argv if argv is not None else sys.argv[1:])

    src = os.path.abspath(args.src)
    if not os.path.isdir(src):
        print("SPZ_GO_INSTALL_FAIL: ship dir missing: " + src)
        return 1

    dest_root = args.dest or find_zbrush_user_scripts()
    if not dest_root:
        print("SPZ_GO_INSTALL_FAIL: could not resolve a ZBrush user scripts folder — pass --dest")
        return 1
    dest = os.path.join(dest_root, DEST_SUBDIR)
    try:
        os.makedirs(dest, exist_ok=True)
        for name in SHIP_FILES:
            s = os.path.join(src, name)
            if not os.path.isfile(s):
                print("SPZ_GO_INSTALL_FAIL: missing ship file " + name)
                return 1
            shutil.copy2(s, os.path.join(dest, name))
    except OSError as e:
        print("SPZ_GO_INSTALL_FAIL: copy failed: " + str(e))
        return 1

    print("SPZ_GO_INSTALL_OK: " + dest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
