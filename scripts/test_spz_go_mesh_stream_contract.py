"""Contracts for the SPZ GO direct mesh stream and FBX compatibility fallback."""

from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
EXTERNAL = ROOT / "External" / "Blender_SpzBridge"
SHIP = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "BlenderBridge"
SOCKET = ROOT / "Assets" / "_gm" / "Features" / "AddonSystem" / "Addon_SocketServer.cs"
HTTP = ROOT / "Assets" / "StreamingAssets" / "AddonSystem" / "http_server.py"
IN_APP = ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "__init__.py"


class SpzGoMeshStreamContractTests(unittest.TestCase):
    def test_blender_ship_files_are_synced(self):
        for name in ("__init__.py", "spz_http.py", "mesh_stream.py", "install_into_blender.py", "blender_manifest.toml"):
            self.assertEqual(
                (EXTERNAL / name).read_bytes(),
                (SHIP / name).read_bytes(),
                f"shipped Blender bridge must match External/{name}",
            )

    def test_receiver_is_bounded_and_materializes_with_bulk_writes(self):
        src = (EXTERNAL / "mesh_stream.py").read_text(encoding="utf-8")
        self.assertIn('PACKET_MAGIC = b"SPZMSH\\x00\\x00"', src)
        self.assertIn("MAX_RAW_BYTES", src)
        self.assertIn("MAX_WIRE_BYTES", src)
        self.assertIn("source.read(raw_size + 1)", src)
        self.assertIn('mesh.vertices.foreach_set("co"', src)
        self.assertIn('mesh.loops.foreach_set("vertex_index"', src)
        self.assertIn('uv_layer.data.foreach_set("uv"', src)
        self.assertNotIn("from_pydata", src)

    def test_rpc_and_http_are_wired(self):
        socket_src = SOCKET.read_text(encoding="utf-8")
        http_src = HTTP.read_text(encoding="utf-8")
        self.assertIn('"spz.cmd.stream_mesh_to_blender"', socket_src)
        self.assertIn("StreamCurrentModelToBlender", socket_src)
        self.assertIn('@app.post("/api/v1/export/mesh_stream"', http_src)
        self.assertIn('"spz.cmd.stream_mesh_to_blender"', http_src)

    def test_in_app_export_streams_before_fbx_and_keeps_fallback(self):
        src = IN_APP.read_text(encoding="utf-8")
        stream_at = src.find('"spz.cmd.stream_mesh_to_blender"')
        fbx_at = src.find('"spz.cmd.export_3d_with_textures_to_path"', stream_at)
        self.assertGreater(stream_at, 0)
        self.assertGreater(fbx_at, stream_at)
        self.assertIn("mesh stream unavailable; continuing with FBX", src)

    def test_blender_pull_prefers_stream_but_retains_fbx(self):
        src = (EXTERNAL / "__init__.py").read_text(encoding="utf-8")
        operator_at = src.find('bl_idname = "spz.go_import"')
        stream_at = src.find("post_mesh_stream", operator_at)
        thread_at = src.find("target=_request_stream_texture_completion", stream_at)
        fallback_at = src.find("# Snapshot before SPZ rewrite", thread_at)
        worker_at = src.find("def _request_stream_texture_completion")
        self.assertGreater(operator_at, 0)
        self.assertGreater(stream_at, operator_at)
        self.assertGreater(thread_at, stream_at)
        self.assertGreater(fallback_at, thread_at)
        self.assertIn("post_export_3d_to_path", src[worker_at:operator_at])
        self.assertIn("_stream_skip_next_ready_fbx = True", src)
        self.assertIn("textures continue in background", src)


if __name__ == "__main__":
    unittest.main()
