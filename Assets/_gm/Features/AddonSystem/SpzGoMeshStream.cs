using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace spz {
	/// <summary>
	/// SPZ GO mesh stream V1. The fixed header is followed by a bounded payload containing
	/// named triangle meshes. Geometry is converted directly from Unity LH/Y-up to the same
	/// Blender coordinates produced by the existing FBX mirror + axis conversion.
	/// </summary>
	public static class SpzGoMeshStream {
		public const ushort ProtocolVersion = 1;
		public const ushort CodecNone = 0;
		public const ushort CodecGzipFast = 1;
		public const int DefaultPort = 5560;
		public const int HeaderBytes = 24;
		public const int MaxMeshCount = 4096;
		public const int MaxNameBytes = 1024;
		public const int MaxRawBytes = 512 * 1024 * 1024;
		public const int MaxWireBytes = 256 * 1024 * 1024;
		public const uint MeshFlagUv0 = 1u;

		static readonly byte[] PacketMagic = { (byte)'S', (byte)'P', (byte)'Z', (byte)'M', (byte)'S', (byte)'H', 0, 0 };
		static readonly byte[] AckMagic = { (byte)'S', (byte)'P', (byte)'Z', (byte)'A', (byte)'C', (byte)'K', 0, 0 };

		sealed class MeshCapture {
			public string Name;
			public Transform Transform;
			public Mesh Mesh;
			public Vector3[] Vertices;
			public int[] Triangles;
			public Vector2[] Uv;
		}

		static long CalculateMeshRawBytes(int nameBytes, int vertexCount, int indexCount, bool hasUv) {
			return 12L
			       + nameBytes
			       + (long)vertexCount * 3L * sizeof(float)
			       + (long)indexCount * sizeof(uint)
			       + (hasUv ? (long)vertexCount * 2L * sizeof(float) : 0L);
		}

		public static bool IsLoopbackHost(string host) {
			if (string.IsNullOrWhiteSpace(host)) return true;
			host = host.Trim();
			if (string.Equals(host, "localhost", System.StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(host, "127.0.0.1", System.StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(host, "::1", System.StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(host, "[::1]", System.StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		public static bool TryBuildPacket(
			GameObject root,
			bool useGzip,
			out byte[] packet,
			out int meshCount,
			out string error
		) {
			packet = null;
			meshCount = 0;
			error = null;
			if (root == null) {
				error = "no current model root";
				return false;
			}

			var captures = new List<MeshCapture>();
			foreach (var filter in root.GetComponentsInChildren<MeshFilter>(includeInactive: true)) {
				if (filter == null || filter.sharedMesh == null) continue;
				var renderer = filter.GetComponent<MeshRenderer>();
				if (renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
				var mesh = filter.sharedMesh;
				if (!mesh.isReadable) {
					error = "mesh is not CPU-readable: " + filter.gameObject.name;
					return false;
				}
				Vector3[] vertices;
				int[] triangles;
				Vector2[] uv;
				try {
					vertices = mesh.vertices;
					triangles = mesh.triangles;
					uv = mesh.uv;
				} catch (Exception ex) {
					error = "could not read mesh '" + filter.gameObject.name + "': " + ex.Message;
					return false;
				}
				if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0)
					continue;
				if ((triangles.Length % 3) != 0) {
					error = "triangle index count is not divisible by three: " + filter.gameObject.name;
					return false;
				}
				captures.Add(new MeshCapture {
					Name = string.IsNullOrEmpty(filter.gameObject.name) ? "SPZ_Mesh" : filter.gameObject.name,
					Transform = filter.transform,
					Mesh = mesh,
					Vertices = vertices,
					Triangles = triangles,
					Uv = uv,
				});
				if (captures.Count > MaxMeshCount) {
					error = "mesh count exceeds protocol limit";
					return false;
				}
			}
			if (captures.Count == 0) {
				error = "no visible readable MeshFilter geometry";
				return false;
			}

			// Snapshot the user's axis basis ONCE: the static accessors read PlayerPrefs, which would
			// cost millions of reads if evaluated per vertex/triangle. Also keeps positions and winding
			// consistent if the user toggles a flip mid-stream.
			var axisBasis = ExportAxisSettings.Snapshot();
			bool reverseWinding = !axisBasis.FlipsHandedness;

			byte[] raw;
			try {
				using (var rawStream = new MemoryStream())
				using (var writer = new BinaryWriter(rawStream, new UTF8Encoding(false), leaveOpen: true)) {
					long projectedRawBytes = 0;
					foreach (var capture in captures) {
						byte[] name = Encoding.UTF8.GetBytes(capture.Name);
						if (name.Length > MaxNameBytes) {
							Array.Resize(ref name, MaxNameBytes);
						}
						bool hasUv = capture.Uv != null && capture.Uv.Length == capture.Vertices.Length;
						projectedRawBytes += CalculateMeshRawBytes(
							name.Length, capture.Vertices.Length, capture.Triangles.Length, hasUv);
						if (projectedRawBytes > MaxRawBytes) {
							error = "raw mesh payload exceeds protocol limit";
							return false;
						}
						writer.Write((ushort)name.Length);
						writer.Write((ushort)(hasUv ? MeshFlagUv0 : 0u));
						writer.Write((uint)capture.Vertices.Length);
						writer.Write((uint)capture.Triangles.Length);
						writer.Write(name);

						// Existing route: mirror Unity Z into FBX, then Blender's -Z/Y axis matrix.
						// Combined direct result is (x, z, y). Bake hierarchy transforms to avoid
						// per-object Euler/handedness ambiguity. Apply the user's shared output-axis
						// permutation/flips last so direct stream and FBX export remain equivalent.
						for (int i = 0; i < capture.Vertices.Length; i++) {
							Vector3 p = capture.Transform.TransformPoint(capture.Vertices[i]);
							Vector3 output = axisBasis.MapOutput(new Vector3(p.x, p.z, p.y));
							writer.Write(output.x);
							writer.Write(output.y);
							writer.Write(output.z);
						}
						for (int i = 0; i < capture.Triangles.Length; i += 3) {
							int a = capture.Triangles[reverseWinding ? i + 2 : i];
							int b = capture.Triangles[i + 1];
							int c = capture.Triangles[reverseWinding ? i : i + 2];
							if ((uint)a >= capture.Vertices.Length
							    || (uint)b >= capture.Vertices.Length
							    || (uint)c >= capture.Vertices.Length) {
								error = "triangle index is outside vertex range: " + capture.Name;
								return false;
							}
							writer.Write((uint)a);
							writer.Write((uint)b);
							writer.Write((uint)c);
						}
						if (hasUv) {
							for (int i = 0; i < capture.Uv.Length; i++) {
								writer.Write(capture.Uv[i].x);
								writer.Write(capture.Uv[i].y);
							}
						}
						if (rawStream.Length > MaxRawBytes) {
							error = "raw mesh payload exceeds protocol limit";
							return false;
						}
					}
					writer.Flush();
					raw = rawStream.ToArray();
				}
			} catch (Exception ex) {
				error = "mesh serialization failed: " + ex.Message;
				return false;
			}

			ushort codec = useGzip ? CodecGzipFast : CodecNone;
			byte[] wire = raw;
			if (useGzip) {
				try {
					using (var compressed = new MemoryStream()) {
						using (var gzip = new GZipStream(
							compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
							gzip.Write(raw, 0, raw.Length);
						wire = compressed.ToArray();
					}
				} catch (Exception ex) {
					error = "mesh compression failed: " + ex.Message;
					return false;
				}
			}
			if (wire.Length > MaxWireBytes) {
				error = "compressed mesh payload exceeds protocol limit";
				return false;
			}

			using (var framed = new MemoryStream(HeaderBytes + wire.Length))
				using (var writer = new BinaryWriter(framed, new UTF8Encoding(false), leaveOpen: true)) {
					writer.Write(PacketMagic);
					writer.Write(ProtocolVersion);
					writer.Write(codec);
					writer.Write((uint)raw.Length);
					writer.Write((uint)wire.Length);
					writer.Write((uint)captures.Count);
					writer.Write(wire);
					writer.Flush();
					packet = framed.ToArray();
				}
			meshCount = captures.Count;
			return true;
		}

		public static bool TrySendPacket(string host, int port, byte[] packet, out string error) {
			error = null;
			if (string.IsNullOrWhiteSpace(host)) host = "127.0.0.1";
			host = host.Trim();
			if (!IsLoopbackHost(host)) {
				error = "mesh stream host must be loopback (127.0.0.1 / ::1 / localhost)";
				return false;
			}
			if (port < 1 || port > 65535) {
				error = "invalid Blender stream port";
				return false;
			}
			if (packet == null || packet.Length < HeaderBytes) {
				error = "empty mesh stream packet";
				return false;
			}
			try {
				using (var client = new TcpClient()) {
					client.NoDelay = true;
					client.SendTimeout = 5000;
					client.ReceiveTimeout = 5000;
					IAsyncResult pending = client.BeginConnect(host, port, null, null);
					try {
						if (!pending.AsyncWaitHandle.WaitOne(1500)) {
							error = "Blender mesh listener did not accept connection";
							return false;
						}
						client.EndConnect(pending);
					} finally {
						pending.AsyncWaitHandle.Close();
					}
					using (NetworkStream stream = client.GetStream()) {
						stream.Write(packet, 0, packet.Length);
						stream.Flush();
						byte[] ack = new byte[12];
						int offset = 0;
						while (offset < ack.Length) {
							int got = stream.Read(ack, offset, ack.Length - offset);
							if (got <= 0) break;
							offset += got;
						}
						if (offset != ack.Length) {
							error = "Blender closed before mesh stream acknowledgement";
							return false;
						}
						for (int i = 0; i < AckMagic.Length; i++) {
							if (ack[i] != AckMagic[i]) {
								error = "invalid Blender mesh stream acknowledgement";
								return false;
							}
						}
						uint status = BitConverter.ToUInt32(ack, 8);
						if (status != 0) {
							error = "Blender rejected mesh stream (status " + status + ")";
							return false;
						}
					}
				}
				return true;
			} catch (Exception ex) {
				error = "mesh stream send failed: " + ex.Message;
				return false;
			}
		}
	}
}
