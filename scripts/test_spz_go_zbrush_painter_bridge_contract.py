"""Contract: ZBrush + Substance Painter file-exchange bridges share the SPZ protocol, stay importable
in plain CPython (DCC APIs guarded), keep host-namespaced exchange folders, and are wired to SPZ-side
installers that flip readiness (spz-go-multi-dcc Phase 2/3).

These are protocol/wiring contracts. The live-DCC mesh round-trip is a separate litmus that needs the
DCC installed, so it is intentionally not asserted here (no false success).
"""
import importlib.util
import os
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
EXT = ROOT / "External"
SHIP = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO"
FASTPATH_CS = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "FastPath_API.cs"
SECTIONS_CS = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "AddonUI_MGR.SpzGoSections.cs"
HOSTS_CS = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "SpzGoHosts.cs"
INSTALL_CS = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "SpzGoBridgeInstall.cs"
UIMGR_CS = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "AddonUI_MGR.cs"

PULL_MARKER = "spz_go_pull_request.json"


def _load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, str(path))
    mod = importlib.util.module_from_spec(spec)
    # Bridges do `import spz_http` when not a package — make the sibling importable.
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(mod)
    finally:
        sys.path.pop(0)
    return mod


class ZBrushPainterBridgeContractTests(unittest.TestCase):
    def test_external_and_ship_copies_synced(self):
        for sub, files in (
            ("ZBrushBridge", ("spz_zbrush_bridge.py", "spz_http.py", "install_into_zbrush.py")),
            ("PainterBridge", ("spz_painter_plugin.py", "spz_http.py", "install_into_painter.py")),
        ):
            ext_dir = EXT / ("ZBrush_SpzBridge" if sub == "ZBrushBridge" else "Painter_SpzBridge")
            for f in files:
                ext_f = ext_dir / f
                ship_f = SHIP / sub / f
                self.assertTrue(ext_f.is_file(), f"missing External file {ext_f}")
                self.assertTrue(ship_f.is_file(), f"missing ship file {ship_f}")
                self.assertEqual(ext_f.read_bytes(), ship_f.read_bytes(),
                                 f"{sub}/{f} ship copy must match External source")

    def test_zbrush_bridge_imports_and_protocol(self):
        z = _load(EXT / "ZBrush_SpzBridge" / "spz_zbrush_bridge.py", "spz_zbrush_bridge")
        self.assertEqual(z.HOST_ID, "zbrush")
        self.assertEqual(z.PULL_REQUEST_NAME, PULL_MARKER)
        self.assertEqual(z.EXCHANGE_SUBDIR, "zbrush")
        self.assertTrue(z.push_mesh_path("X").endswith(os.path.join("X", "from_zbrush.obj")))
        self.assertTrue(z.spz_pull_fbx("X").endswith(os.path.join("X", "from_spz.fbx")))
        # DCC op must be guarded (no live ZBrush here) and must not raise.
        ok, _ = z._zbrush_export_active_tool("X")
        self.assertFalse(ok)
        for fn in ("spz_import", "spz_export", "spz_poll_pull_request", "create_palette"):
            self.assertTrue(callable(getattr(z, fn)))
        # Palette registration must no-op (return False) outside ZBrush.
        self.assertFalse(z.create_palette())

    def test_zbrush_bridge_uses_confirmed_2026_python_api(self):
        # Spike result: ZBrush 2026 ships `zbrush.commands` (stubs at
        # Documentation/python-api/stubs/zbrush/commands.pyi). Pin the confirmed calls so the mesh ops
        # can't silently regress back to guessed entry points (export_tool / run_zscript / IPress).
        src = (EXT / "ZBrush_SpzBridge" / "spz_zbrush_bridge.py").read_text(encoding="utf-8")
        self.assertIn("import zbrush.commands", src)
        self.assertIn("set_next_filename", src)
        self.assertIn('press("Tool:Export")', src)
        self.assertIn('press("Tool:Import")', src)
        self.assertIn("add_subpalette", src)
        self.assertIn("add_button", src)
        # Old guesswork must be gone.
        self.assertNotIn("run_zscript", src)
        self.assertNotIn("export_tool", src)
        self.assertNotIn("IPress,Tool:Export", src)

    def test_painter_bridge_imports_and_protocol(self):
        p = _load(EXT / "Painter_SpzBridge" / "spz_painter_plugin.py", "spz_painter_plugin")
        self.assertEqual(p.HOST_ID, "painter")
        self.assertEqual(p.PULL_REQUEST_NAME, PULL_MARKER)
        self.assertEqual(p.EXCHANGE_SUBDIR, "painter")
        self.assertEqual(p.PACK_LABEL_MAP["basecolor"], "albedo")
        self.assertEqual(p.PACK_LABEL_MAP["roughness"], "roughness")
        # Plugin lifecycle exists and start/close are safe without Painter/Qt.
        self.assertTrue(callable(p.start_plugin))
        self.assertTrue(callable(p.close_plugin))
        p.start_plugin()
        p.close_plugin()
        # Export must refuse without UVs / Painter (no false success).
        ok, _ = p._painter_export_textures_and_mesh("X")
        self.assertFalse(ok)

    def test_painter_pull_keeps_marker_when_export_fails(self):
        # Delete-before-push burned every SPZ Import on the first watcher tick while Painter export
        # still fails closed (spike). Keep the marker on failure; only clear it after a successful push.
        import tempfile
        p = _load(EXT / "Painter_SpzBridge" / "spz_painter_plugin.py", "spz_painter_plugin_pull")
        with tempfile.TemporaryDirectory() as td:
            req = os.path.join(td, PULL_MARKER)
            with open(req, "w", encoding="utf-8") as f:
                f.write('{"host":"painter"}')
            p.spz_export = lambda: (False, "spike not ready")  # type: ignore
            self.assertFalse(p._consume_pull_request(td))
            self.assertTrue(os.path.isfile(req), "failed push must leave the marker for a later retry")
            # Same fingerprint must not re-spam every poll.
            self.assertFalse(p._consume_pull_request(td))
            p.spz_export = lambda: (True, "ok")  # type: ignore
            # Touch the request so the debounce sees a fresh Import click.
            with open(req, "w", encoding="utf-8") as f:
                f.write('{"host":"painter","retry":1}')
            self.assertTrue(p._consume_pull_request(td))
            self.assertFalse(os.path.isfile(req), "successful push must clear the marker")

    def test_export_http_passes_host_id_for_axis_basis(self):
        # Without host_id, Unity reused whichever host last applied the shared ExportAxisSettings.
        for sub, host in (
            ("ZBrush_SpzBridge/spz_http.py", "zbrush"),
            ("Painter_SpzBridge/spz_http.py", "painter"),
            ("Blender_SpzBridge/spz_http.py", "blender"),
        ):
            src = (EXT / sub).read_text(encoding="utf-8")
            self.assertIn('body["host_id"]', src, sub)
            self.assertIn("host_id: Optional[str]", src, sub)
        zb = (EXT / "ZBrush_SpzBridge" / "spz_zbrush_bridge.py").read_text(encoding="utf-8")
        self.assertIn("host_id=HOST_ID", zb)
        pn = (EXT / "Painter_SpzBridge" / "spz_painter_plugin.py").read_text(encoding="utf-8")
        self.assertIn("host_id=HOST_ID", pn)
        bl = (EXT / "Blender_SpzBridge" / "__init__.py").read_text(encoding="utf-8")
        self.assertIn('host_id="blender"', bl)

    def test_installers_target_user_dirs_not_program_files(self):
        for name in ("ZBrush_SpzBridge/install_into_zbrush.py", "Painter_SpzBridge/install_into_painter.py"):
            src = (EXT / name).read_text(encoding="utf-8")
            # Resolve into the user profile / Documents, never construct a Program Files target.
            self.assertIn("expanduser", src)
            self.assertNotIn('join("C:', src)
            self.assertNotIn('"Program Files"', src)  # no literal used as a path segment
            self.assertIn("SPZ_GO_INSTALL_OK", src)
            self.assertIn("SPZ_GO_INSTALL_FAIL", src)

    def test_zbrush_resolver_targets_confirmed_public_zbrushdata(self):
        # Spike result: ZBrush 2026 user data lives under Public Documents (ZBrushData2026), not the
        # personal Documents folder. Both resolvers must look there.
        py = (EXT / "ZBrush_SpzBridge" / "install_into_zbrush.py").read_text(encoding="utf-8")
        self.assertIn("PUBLIC", py)
        self.assertIn("zbrushdata", py.lower())
        cs = FASTPATH_CS.read_text(encoding="utf-8")
        body = cs.split("FindZBrushUserScriptsDir")[1].split("catch")[0]
        self.assertIn("PUBLIC", body)
        self.assertIn("ZBrushData", body)

    def test_painter_resolver_does_not_invent_plugins_dir(self):
        py = (EXT / "Painter_SpzBridge" / "install_into_painter.py").read_text(encoding="utf-8")
        self.assertNotIn("return roots[0]", py)
        self.assertIn('return ""', py)
        cs = FASTPATH_CS.read_text(encoding="utf-8")
        body = cs.split("FindPainterPluginsDir")[1].split("TryInstallSpzGoBridgeByCopy")[0]
        self.assertNotIn("return candidates[0]", body)

    def test_spz_side_installers_and_readiness_wired(self):
        fp = FASTPATH_CS.read_text(encoding="utf-8")
        self.assertIn("TryInstallSpzGoZBrushBridge", fp)
        self.assertIn("TryInstallSpzGoPainterBridge", fp)
        self.assertIn("FindZBrushUserScriptsDir", fp)
        self.assertIn("FindPainterPluginsDir", fp)
        self.assertIn("SpzGoBridgeInstall.MarkInstalled", fp)
        # Never elevate: no Program Files target in the resolvers.
        self.assertNotIn("ProgramFiles", fp.split("FindZBrushUserScriptsDir")[1].split("}")[0])

        hosts = HOSTS_CS.read_text(encoding="utf-8")
        self.assertIn("BridgeInstalledProbe", hosts)
        self.assertIn("public static bool IsBridgeReady", hosts)

        install = INSTALL_CS.read_text(encoding="utf-8")
        self.assertIn("MarkInstalled", install)
        self.assertIn("IsInstalled", install)

        ui = UIMGR_CS.read_text(encoding="utf-8")
        self.assertIn("SpzGoHosts.BridgeInstalledProbe = SpzGoBridgeInstall.IsInstalled", ui)
        self.assertIn("do_install_zbrush_bridge", ui)
        self.assertIn("do_install_painter_bridge", ui)

        sections = SECTIONS_CS.read_text(encoding="utf-8")
        self.assertIn("Install into ZBrush", sections)
        self.assertIn("Install into Substance Painter", sections)
        self.assertIn("SpzGoHosts.IsBridgeReady", sections)


if __name__ == "__main__":
    unittest.main()
