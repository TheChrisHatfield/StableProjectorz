using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace spz.EditorTests {
	public class SpzGoMeshStreamContractTests {
		[Test]
		public void V1Packet_GzipFramesConvertedGeometryAndReversedWinding() {
			var root = new GameObject("Root");
			var child = new GameObject("Triangle");
			child.transform.SetParent(root.transform, false);
			child.transform.localPosition = new Vector3(10f, 20f, 30f);
			var filter = child.AddComponent<MeshFilter>();
			child.AddComponent<MeshRenderer>();
			var mesh = new Mesh {
				name = "TriangleMesh",
				vertices = new[] {
					new Vector3(1f, 2f, 3f),
					new Vector3(4f, 5f, 6f),
					new Vector3(7f, 8f, 9f),
				},
				uv = new[] { Vector2.zero, Vector2.right, Vector2.up },
				triangles = new[] { 0, 1, 2 },
			};
			filter.sharedMesh = mesh;

			try {
				Assert.That(
					SpzGoMeshStream.TryBuildPacket(root, true, out var packet, out var meshCount, out var error),
					Is.True,
					error);
				Assert.That(meshCount, Is.EqualTo(1));

				using var framed = new BinaryReader(new MemoryStream(packet));
				Assert.That(framed.ReadBytes(8), Is.EqualTo(new byte[] {
					(byte)'S', (byte)'P', (byte)'Z', (byte)'M', (byte)'S', (byte)'H', 0, 0
				}));
				Assert.That(framed.ReadUInt16(), Is.EqualTo(SpzGoMeshStream.ProtocolVersion));
				Assert.That(framed.ReadUInt16(), Is.EqualTo(SpzGoMeshStream.CodecGzipFast));
				uint rawSize = framed.ReadUInt32();
				uint wireSize = framed.ReadUInt32();
				Assert.That(framed.ReadUInt32(), Is.EqualTo(1u));
				byte[] wire = framed.ReadBytes((int)wireSize);

				byte[] raw;
				using (var output = new MemoryStream()) {
					using (var gzip = new GZipStream(new MemoryStream(wire), CompressionMode.Decompress))
						gzip.CopyTo(output);
					raw = output.ToArray();
				}
				Assert.That(raw.Length, Is.EqualTo((int)rawSize));
				using var reader = new BinaryReader(new MemoryStream(raw));
				ushort nameBytes = reader.ReadUInt16();
				Assert.That(reader.ReadUInt16(), Is.EqualTo((ushort)SpzGoMeshStream.MeshFlagUv0));
				Assert.That(reader.ReadUInt32(), Is.EqualTo(3u));
				Assert.That(reader.ReadUInt32(), Is.EqualTo(3u));
				Assert.That(System.Text.Encoding.UTF8.GetString(reader.ReadBytes(nameBytes)), Is.EqualTo("Triangle"));

				// Unity world (11,22,33) -> Blender direct (x,z,y).
				Assert.That(reader.ReadSingle(), Is.EqualTo(11f));
				Assert.That(reader.ReadSingle(), Is.EqualTo(33f));
				Assert.That(reader.ReadSingle(), Is.EqualTo(22f));
				reader.BaseStream.Position += 2 * 3 * sizeof(float);
				Assert.That(reader.ReadUInt32(), Is.EqualTo(2u));
				Assert.That(reader.ReadUInt32(), Is.EqualTo(1u));
				Assert.That(reader.ReadUInt32(), Is.EqualTo(0u));
			} finally {
				Object.DestroyImmediate(mesh);
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void V1Packet_RejectsMissingGeometry() {
			var root = new GameObject("Empty");
			try {
				Assert.That(
					SpzGoMeshStream.TryBuildPacket(root, false, out _, out _, out var error),
					Is.False);
				Assert.That(error, Does.Contain("no visible readable"));
			} finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void RawPayloadSize_IsPreflightedWithoutAllocatingHugeBuffers() {
			var calculate = typeof(SpzGoMeshStream)
				.GetMethod("CalculateMeshRawBytes", BindingFlags.Static | BindingFlags.NonPublic);
			long bytes = (long)calculate.Invoke(null, new object[] {
				SpzGoMeshStream.MaxNameBytes,
				int.MaxValue,
				int.MaxValue,
				true,
			});
			Assert.That(bytes, Is.GreaterThan(SpzGoMeshStream.MaxRawBytes));

			string source = File.ReadAllText(
				Path.Combine(Application.dataPath, "_gm/Features/AddonSystem/SpzGoMeshStream.cs"));
			int preflightAt = source.IndexOf("projectedRawBytes > MaxRawBytes", System.StringComparison.Ordinal);
			int firstVertexWriteAt = source.IndexOf("writer.Write(p.x)", System.StringComparison.Ordinal);
			Assert.That(preflightAt, Is.GreaterThan(0));
			Assert.That(firstVertexWriteAt, Is.GreaterThan(preflightAt));
		}

		[Test]
		public void StreamCommand_RefusesWhileTextureExportOwnsSavePipeline() {
			var saveGo = new GameObject("Save");
			var fastGo = new GameObject("FastPath");
			var save = saveGo.AddComponent<Save_MGR>();
			var fast = fastGo.AddComponent<FastPath_API>();
			var instanceField = typeof(Save_MGR)
				.GetField("<instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
			var previousSave = Save_MGR.instance;
			try {
				instanceField.SetValue(null, save);
				typeof(FastPath_API)
					.GetField("_isInitialized", BindingFlags.Instance | BindingFlags.NonPublic)
					.SetValue(fast, true);
				typeof(Save_MGR)
					.GetProperty("_isSaving", BindingFlags.Instance | BindingFlags.Public)
					.SetValue(save, true);

				Assert.That(
					fast.StreamCurrentModelToBlender(
						"127.0.0.1", SpzGoMeshStream.DefaultPort, true, out _, out var error),
					Is.False);
				Assert.That(error, Does.Contain("in progress"));
			} finally {
				instanceField.SetValue(null, previousSave);
				Object.DestroyImmediate(fastGo);
				Object.DestroyImmediate(saveGo);
			}
		}
	}
}
