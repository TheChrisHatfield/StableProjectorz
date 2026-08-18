# SPDX-License-Identifier: MIT
"""
Copy the SPZ GO Substance Painter plugin into the user's Painter python/plugins folder.

Standalone (does not launch Painter):

  python install_into_painter.py --src <PainterBridgeDir> [--dest <PainterPluginsDir>]

Stdout markers:
  SPZ_GO_INSTALL_OK: <dest>
  SPZ_GO_INSTALL_FAIL: <reason>

Targets user Documents only — never Program Files.
When several Painter user trees exist, the highest version in the folder name wins
(then newest mtime). Never invents a Documents/Adobe/... path Painter never created.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import sys
from typing import Iterable, List, Tuple

SHIP_FILES = ("spz_painter_plugin.py", "spz_http.py")


def _is_painter_user_root_name(folder_name: str) -> bool:
    low = (folder_name or "").lower()
    return "painter" in low and ("substance" in low or "allegorithmic" in low)


def _parse_painter_version(path_or_name: str) -> Tuple[int, ...]:
    # Only the number after "Painter" — ignore the "3" in "3D".
    name = os.path.basename(path_or_name.rstrip("\\/"))
    m = re.search(r"Painter\s+(\d+(?:\.\d+)*)", name, re.I)
    if not m:
        return (0,)
    parts = [int(x) for x in m.group(1).split(".") if x.isdigit()]
    return tuple(parts[:4]) if parts else (0,)


def _collect_painter_user_roots(docs: str) -> List[str]:
    roots: List[str] = []
    fixed = (
        os.path.join(docs, "Adobe", "Adobe Substance 3D Painter"),
        os.path.join(docs, "Allegorithmic", "Substance Painter"),
        os.path.join(docs, "Substance 3D Painter"),
    )
    for r in fixed:
        if os.path.isdir(r):
            roots.append(r)

    def scan_vendor(vendor: str) -> None:
        if not os.path.isdir(vendor):
            return
        try:
            for name in os.listdir(vendor):
                full = os.path.join(vendor, name)
                if os.path.isdir(full) and _is_painter_user_root_name(name):
                    roots.append(full)
        except OSError:
            pass

    scan_vendor(os.path.join(docs, "Adobe"))
    scan_vendor(os.path.join(docs, "Allegorithmic"))
    return roots


def pick_painter_plugins_dir(documents_roots: Iterable[str]) -> str:
    """Return python/plugins under the newest existing Painter user tree, or ''."""
    painter_roots: List[str] = []
    for docs in documents_roots:
        if docs and os.path.isdir(docs):
            painter_roots.extend(_collect_painter_user_roots(docs))
    # De-dupe while preserving paths
    seen = set()
    uniq: List[str] = []
    for r in painter_roots:
        key = os.path.normcase(os.path.normpath(r))
        if key in seen:
            continue
        seen.add(key)
        uniq.append(r)
    if not uniq:
        return ""

    def sort_key(p: str):
        try:
            mtime = os.path.getmtime(p)
        except OSError:
            mtime = 0.0
        return (_parse_painter_version(p), mtime)

    best = max(uniq, key=sort_key)
    return os.path.join(best, "python", "plugins")


def find_painter_plugins_dir() -> str:
    """User-writable Substance Painter python/plugins folder. '' when unknown."""
    home = os.path.expanduser("~")
    docs = os.path.join(home, "Documents")
    return pick_painter_plugins_dir([docs])


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
        print(
            "SPZ_GO_INSTALL_FAIL: could not resolve a Painter plugins folder — "
            "open Substance Painter once (to create its Documents tree) or pass --dest"
        )
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
    raise SystemExit(main())
