"""SPZ GO re-import must replace the previous SPZ model instead of stacking duplicates.

Executes the real _try_import_exchange_fbx source against a stub bpy, so the replacement
logic is exercised rather than just pattern-matched.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BRIDGES = [
	ROOT / "External" / "Blender_SpzBridge" / "__init__.py",
	ROOT / "Assets" / "StreamingAssets" / "Addons" / "StableProjectorzGO" / "BlenderBridge" / "__init__.py",
]


class FakeMesh:
	def __init__(self, name):
		self.name = name
		self.users = 1


class FakeObj:
	def __init__(self, name, obj_type="MESH", data=None):
		self.name = name
		self.type = obj_type
		self.data = data
		self._props = {}

	def get(self, key, default=None):
		return self._props.get(key, default)

	def __setitem__(self, key, value):
		self._props[key] = value

	def __repr__(self):
		return f"<FakeObj {self.name}>"


class FakeCollection(list):
	def remove(self, obj, do_unlink=False):
		list.remove(self, obj)
		if getattr(obj, "data", None) is not None:
			obj.data.users -= 1


class FakeMeshCollection(list):
	def remove(self, mesh):
		list.remove(self, mesh)


class FakeData:
	def __init__(self):
		self.objects = FakeCollection()
		self.meshes = FakeMeshCollection()


class FakeBpy:
	def __init__(self):
		self.data = FakeData()


def _load_import_fn(bridge_path, bpy_stub, import_result, spawn):
	"""Exec the real replacement helpers + _try_import_exchange_fbx with stubbed collaborators."""
	src = bridge_path.read_text(encoding="utf-8")
	start = src.index('_SPZ_IMPORT_MARKER = "spz_go_import"')
	end = src.index("\ndef _parse_ready_stamp(", start)

	calls = {"texture": 0}

	class _FbxOp:
		@staticmethod
		def fbx(**kwargs):
			spawn()
			return import_result

	class _Ops:
		import_scene = _FbxOp()

	bpy_stub.ops = _Ops()

	ns = {
		"bpy": bpy_stub,
		"_FBX_AXIS_FORWARD": "-Z",
		"_FBX_AXIS_UP": "Y",
		"_apply_spz_export_scale_litmus": lambda path: None,
		"_auto_apply_exchange_texture_after_import": lambda path: calls.__setitem__("texture", calls["texture"] + 1),
		"_mark_exchange_stamp_seen_for_fbx": lambda path: None,
		"print": lambda *a, **k: None,
	}
	exec(src[start:end], ns)
	return ns["_try_import_exchange_fbx"], calls


class SpzGoImportReplacesPreviousTests(unittest.TestCase):

	def test_bridge_copies_stay_in_sync(self):
		texts = [p.read_text(encoding="utf-8") for p in BRIDGES]
		self.assertEqual(texts[0], texts[1],
			"the bundled BlenderBridge is what gets installed; it must match External/")

	def test_stream_also_supersedes_fbx_imported_model(self):
		# Replacement has to be symmetric: importing then streaming must not duplicate either.
		for name in ("External/Blender_SpzBridge", "Assets/StreamingAssets/Addons/StableProjectorzGO/BlenderBridge"):
			with self.subTest(bridge=name):
				src = (ROOT / name / "mesh_stream.py").read_text(encoding="utf-8")
				start = src.index("def _remove_previous_stream_objects(")
				end = src.index("\ndef materialize_next(", start)
				body = src[start:end]
				self.assertIn("spz_mesh_stream", body)
				self.assertIn("spz_go_import", body,
					"a stream must also clear a model that arrived via FBX import")

	def _run_import(self, bridge, existing, import_result="FINISHED", spawn_count=1):
		bpy_stub = FakeBpy()
		for obj in existing:
			bpy_stub.data.objects.append(obj)
			if obj.data is not None:
				bpy_stub.data.meshes.append(obj.data)

		def spawn():
			for i in range(spawn_count):
				mesh = FakeMesh(f"NewMesh{i}")
				obj = FakeObj(f"NewObj{i}", data=mesh)
				bpy_stub.data.meshes.append(mesh)
				bpy_stub.data.objects.append(obj)

		fn, calls = _load_import_fn(bridge, bpy_stub,
			{import_result} if import_result else None, spawn)
		ok = fn("C:/exchange/from_spz.fbx")
		return ok, bpy_stub, calls

	def test_reimport_replaces_previous_spz_objects(self):
		for bridge in BRIDGES:
			with self.subTest(bridge=bridge.name):
				mesh = FakeMesh("OldMesh")
				old = FakeObj("OldSpz", data=mesh)
				old["spz_go_import"] = True
				ok, bpy_stub, _ = self._run_import(bridge, [old])

				self.assertTrue(ok)
				names = [o.name for o in bpy_stub.data.objects]
				self.assertNotIn("OldSpz", names,
					"a second export/import must not leave the previous SPZ copy behind")
				self.assertEqual(names, ["NewObj0"])
				# The orphaned mesh datablock must go too, or repeated cycles bloat the file.
				self.assertEqual([m.name for m in bpy_stub.data.meshes], ["NewMesh0"])

	def test_reimport_also_supersedes_streamed_geometry(self):
		for bridge in BRIDGES:
			with self.subTest(bridge=bridge.name):
				streamed = FakeObj("StreamedSpz", data=FakeMesh("StreamMesh"))
				streamed["spz_mesh_stream"] = True
				ok, bpy_stub, _ = self._run_import(bridge, [streamed])
				self.assertTrue(ok)
				self.assertEqual([o.name for o in bpy_stub.data.objects], ["NewObj0"])

	def test_user_objects_are_never_removed(self):
		for bridge in BRIDGES:
			with self.subTest(bridge=bridge.name):
				user_obj = FakeObj("UserCube", data=FakeMesh("UserMesh"))
				spz_obj = FakeObj("OldSpz", data=FakeMesh("OldMesh"))
				spz_obj["spz_go_import"] = True
				ok, bpy_stub, _ = self._run_import(bridge, [user_obj, spz_obj])
				self.assertTrue(ok)
				names = [o.name for o in bpy_stub.data.objects]
				self.assertIn("UserCube", names, "unmarked user geometry must survive an SPZ import")
				self.assertNotIn("OldSpz", names)

	def test_failed_import_leaves_previous_model_intact(self):
		for bridge in BRIDGES:
			with self.subTest(bridge=bridge.name):
				old = FakeObj("OldSpz", data=FakeMesh("OldMesh"))
				old["spz_go_import"] = True
				ok, bpy_stub, calls = self._run_import(
					bridge, [old], import_result="CANCELLED", spawn_count=0)
				self.assertFalse(ok)
				self.assertIn("OldSpz", [o.name for o in bpy_stub.data.objects],
					"a cancelled import must not delete the model already in the scene")
				self.assertEqual(calls["texture"], 0)

	def test_new_objects_are_marked_for_the_next_replacement(self):
		for bridge in BRIDGES:
			with self.subTest(bridge=bridge.name):
				ok, bpy_stub, _ = self._run_import(bridge, [], spawn_count=2)
				self.assertTrue(ok)
				self.assertEqual(len(bpy_stub.data.objects), 2)
				for obj in bpy_stub.data.objects:
					self.assertTrue(obj.get("spz_go_import"),
						"unmarked imports would never be replaced on the next cycle")


if __name__ == "__main__":
	unittest.main()
