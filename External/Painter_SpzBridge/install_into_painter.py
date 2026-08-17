# SPDX-License-Identifier: MIT
"""
Copy the SPZ GO Substance Painter plugin into the user's Painter python/plugins folder.

Standalone (does not launch Painter):

  python install_into_painter.py --src <PainterBridgeDir> [--dest <PainterPluginsDir>]

Stdout markers:
  SPZ_GO_INSTALL_OK: <dest>
  SPZ_GO_INSTALL_FAIL: <reason>

Targets user Documents only — never Program Files.
"""

from __future__ import annotations

import argparse
import os
import shutil
import sys

SHIP_FILES = ("spz_painter_plugin.py", "spz_http.py")


def find_painter_plugins_dir() -> str:
    """User-writable Substance Painter python/plugins folder (Windows/macOS/Linux). '' when unknown."""
    home = os.path.expanduser("~")
    docs = os.path.join(home, "Documents")
    roots = [
        os.path.join(docs, "Adobe", "Adobe Substance 3D Painter", "python", "plugins"),
        os.path.join(docs, "Allegorithmic", "Substance Painter", "python", "plugins"),
        os.path.join(docs, "Substance 3D Painter", "python", "plugins"),
    ]
    for r in roots:
        if os.path.isdir(os.path.dirname(os.path.dirname(r))):
            return r
    # Do not invent Documents/Adobe/... — Painter never created that tree, so a copy there would
    # light SPZ's logo for a folder Painter never loads. Caller must pass --dest instead.
    return ""


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Install SPZ GO Substance Painter plugin")
    ap.add_argument("--src", required=True, help="Path to shipped PainterBridge folder")
    ap.add_argument("--dest", default="", help="Painter python/plugins dir (auto-resolved if omitted)")
    args = ap.parse_args(argv if argv is not None else sys.argv[1:])

    src = os.path.abspath(args.src)
    if not os.path.isdir(src):
        print("SPZ_GO_INSTALL_FAIL: ship dir missing: " + src)
        return 1

    dest = args.dest or find_painter_plugins_dir()
    if not dest:
        print("SPZ_GO_INSTALL_FAIL: could not resolve Painter python/plugins — pass --dest")
        return 1
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
