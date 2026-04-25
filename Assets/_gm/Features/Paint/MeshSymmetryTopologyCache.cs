using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// True bilateral symmetry from mesh <b>ground truth</b>: on load/selection we scan <see cref="Mesh.vertices"/>,
	/// build per-vertex symmetric partners in mesh local space (X mirror through object YZ), then at paint time we
	/// find the source location on the original triangulation (not the convex-hull collider) and map to the
	/// analogous barycentric mix on the paired vertex triple. This is the correct foundation for “exact” bilateral
	/// on authored symmetric meshes, independent of the Physics convex hull. Falls back to geometric paths when
	/// the mesh is not readable, too large, or pairing is missing.
	/// </summary>
	public static class MeshSymmetryTopologyCache {

		class Entry {
			public int[] pairIndex;   // -1 = no pair
			public int meshInstanceId;
			public int lastVertexCount;
			public int axis;
		}

		static readonly Dictionary<int, Entry> _byHost = new Dictionary<int, Entry>();
		static bool _hooked;

		static void EnsureHook () {
			if (_hooked) return;
			_hooked = true;
			SD_3D_Mesh.Act_OnWillDestroyMesh += m => {
				if (m == null) return;
				_byHost.Remove(m.GetInstanceID());
			};
		}

		/// <summary>Invalidate cache for this host (e.g. mesh swap on same object).</summary>
		public static void Invalidate (SD_3D_Mesh host) {
			if (host == null) return;
			_byHost.Remove(host.GetInstanceID());
		}

		enum MirrorAxis { X = 0, Y = 1, Z = 2 }

		static void MirrorLocalOnAxis (ref Vector3 p, MirrorAxis axis) {
			switch (axis) {
				case MirrorAxis.Y: p.y = -p.y; break;
				case MirrorAxis.Z: p.z = -p.z; break;
				default: p.x = -p.x; break;
			}
		}

		static bool TryResolveAxisFromPlaneNormalLocal (Vector3 localPlaneNormal, out MirrorAxis axis) {
			axis = MirrorAxis.X;
			float ax = Mathf.Abs(localPlaneNormal.x);
			float ay = Mathf.Abs(localPlaneNormal.y);
			float az = Mathf.Abs(localPlaneNormal.z);
			float mx = Mathf.Max(ax, Mathf.Max(ay, az));
			// Topology pairing only supports principal bilateral axes.
			if (mx < 0.90f)
				return false;
			if (mx == ay) axis = MirrorAxis.Y;
			else if (mx == az) axis = MirrorAxis.Z;
			return true;
		}

		/// <summary>Closest point on triangle in 3D, returns bary (u at a, v at b, w at c).</summary>
		/// <remarks>Vertex/edge cases after Christer Ericson (RTCD); face case = bary of orthogonal plane projection (not 3× cross magnitudes, which is wrong in 3D).</remarks>
		static void ClosestPointBary (Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float u, out float v, out float w) {
			Vector3 ab = b - a, ac = c - a, ap = p - a;
			float d1 = Vector3.Dot(ab, ap);
			float d2 = Vector3.Dot(ac, ap);
			if (d1 <= 0f && d2 <= 0f) { u = 1f; v = 0f; w = 0f; return; }
			Vector3 bp = p - b;
			float d3 = Vector3.Dot(ab, bp);
			float d4 = Vector3.Dot(ac, bp);
			if (d3 >= 0f && d4 <= d3) { u = 0f; v = 1f; w = 0f; return; }
			Vector3 cp = p - c;
			float d5 = Vector3.Dot(ab, cp);
			float d6 = Vector3.Dot(ac, cp);
			if (d6 >= 0f && d5 <= d6) { u = 0f; v = 0f; w = 1f; return; }
			float vc = d1 * d4 - d3 * d2;
			if (vc <= 0f && d1 >= 0f && d3 <= 0f) {
				float t = d1 / (d1 - d3);
				u = 1f - t; v = t; w = 0f; return;
			}
			float vb = d5 * d2 - d1 * d6;
			if (vb <= 0f && d2 >= 0f && d6 <= 0f) {
				float t = d2 / (d2 - d6);
				u = 1f - t; v = 0f; w = t; return;
			}
			float va = d3 * d6 - d5 * d4;
			if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) {
				float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
				u = 0f; v = 1f - t; w = t; return;
			}
			Vector3 n = Vector3.Cross(ab, ac);
			float d = Vector3.Dot(n, n);
			if (d < 1e-20f) { u = v = w = 1f / 3f; return; }
			Vector3 p0 = p - n * (Vector3.Dot(n, p - a) / d);
			var v0 = b - a;
			var v1 = c - a;
			var v2 = p0 - a;
			float d00 = Vector3.Dot(v0, v0);
			float d01 = Vector3.Dot(v0, v1);
			float d11 = Vector3.Dot(v1, v1);
			float d20 = Vector3.Dot(v2, v0);
			float d21 = Vector3.Dot(v2, v1);
			float denom2 = d00 * d11 - d01 * d01;
			if (Mathf.Abs(denom2) < 1e-20f) { u = v = w = 1f / 3f; return; }
			v = (d11 * d20 - d01 * d21) / denom2;
			w = (d00 * d21 - d01 * d20) / denom2;
			u = 1f - v - w;
			u = Mathf.Max(0f, u);
			v = Mathf.Max(0f, v);
			w = Mathf.Max(0f, w);
			float tsum = u + v + w;
			if (tsum > 1e-8f) { u /= tsum; v /= tsum; w /= tsum; }
			else { u = v = w = 1f / 3f; }
		}

		static Entry BuildEntry (SD_3D_Mesh host, Mesh mesh, MirrorAxis mirrorAxis) {
			if (mesh == null || !mesh.isReadable) return null;
			int nV = mesh.vertexCount;
			if (nV < 3 || nV > 200_000) return null;
			var vl = mesh.vertices;
			if (vl == null || vl.Length != nV) return null;
			var b = mesh.bounds;
			float size = b.size.magnitude;
			float pairEpsSqr = Mathf.Pow(Mathf.Max(1e-6f, size * 2.5e-4f), 2f);
			// Voxel index for 3D hash (fast nearest on reflected query)
			float inv = 32f / Mathf.Max(1e-5f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
			Vector3 mn = b.min, mx = b.max;
			var map = new Dictionary<int, List<int>>();

			int Key (Vector3 p) {
				int ix = Mathf.Clamp((int)((p.x - mn.x) * inv), 0, 31);
				int iy = Mathf.Clamp((int)((p.y - mn.y) * inv), 0, 31);
				int iz = Mathf.Clamp((int)((p.z - mn.z) * inv), 0, 31);
				return (ix * 32 + iy) * 32 + iz;
			}
			for (int i = 0; i < nV; i++) {
				int k = Key(vl[i]);
				if (!map.TryGetValue(k, out var list)) {
					list = new List<int>(4);
					map[k] = list;
				}
				list.Add(i);
			}

			var pair = new int[nV];
			for (int i = 0; i < nV; i++) pair[i] = -1;
			for (int i = 0; i < nV; i++) {
				Vector3 r = vl[i];
				MirrorLocalOnAxis(ref r, mirrorAxis);
				int kc = Key(r);
				int kx = (kc / (32 * 32)) % 32, ky = (kc / 32) % 32, kz = kc % 32;
				float bestD = float.MaxValue;
				int bestJ = -1;
				for (int ddx = -1; ddx <= 1; ddx++) {
					for (int ddy = -1; ddy <= 1; ddy++) {
						for (int ddz = -1; ddz <= 1; ddz++) {
							int ix = Mathf.Clamp(kx + ddx, 0, 31);
							int iy = Mathf.Clamp(ky + ddy, 0, 31);
							int iz = Mathf.Clamp(kz + ddz, 0, 31);
							int k = (ix * 32 + iy) * 32 + iz;
							if (!map.TryGetValue(k, out var list)) continue;
							for (int t = 0; t < list.Count; t++) {
								int j = list[t];
								float d2 = (vl[j] - r).sqrMagnitude;
								if (d2 < bestD && d2 < pairEpsSqr) {
									if (d2 < bestD - 1e-12f || (d2 < bestD + 1e-12f && j < bestJ)) {
										bestD = d2; bestJ = j;
									}
								}
							}
						}
					}
				}
				if (bestD < float.MaxValue) pair[i] = bestJ;
			}
			// Strengthen: midline vertex maps to self if mirror lands on self
			for (int i = 0; i < nV; i++) {
				if (pair[i] >= 0) continue;
				float dAxis = mirrorAxis == MirrorAxis.X ? vl[i].x : (mirrorAxis == MirrorAxis.Y ? vl[i].y : vl[i].z);
				float axisSize = mirrorAxis == MirrorAxis.X ? Mathf.Max(1e-6f, b.size.x) : (mirrorAxis == MirrorAxis.Y ? Mathf.Max(1e-6f, b.size.y) : Mathf.Max(1e-6f, b.size.z));
				if (Mathf.Abs(dAxis) * inv * axisSize < 0.02f && pair[i] < 0) {
					Vector3 r2 = vl[i];
					MirrorLocalOnAxis(ref r2, mirrorAxis);
					if ((r2 - vl[i]).sqrMagnitude < pairEpsSqr) pair[i] = i;
				}
			}
			// Reciprocal consistency: invalidate one-way / conflicted matches to prevent offset drift.
			for (int i = 0; i < nV; i++) {
				int j = pair[i];
				if (j < 0 || j >= nV)
					continue;
				if (pair[j] != i) {
					pair[i] = -1;
				}
			}
			return new Entry {
				pairIndex = pair, meshInstanceId = mesh.GetInstanceID(), lastVertexCount = nV, axis = (int)mirrorAxis
			};
		}

		static Entry GetOrBuild (SD_3D_Mesh host, MirrorAxis mirrorAxis) {
			if (host == null || host._sharedMesh == null) return null;
			EnsureHook();
			int hid = host.GetInstanceID();
			if (_byHost.TryGetValue(hid, out var e)) {
				if (e != null && e.lastVertexCount == host._sharedMesh.vertexCount
				    && e.meshInstanceId == host._sharedMesh.GetInstanceID()
				    && e.axis == (int)mirrorAxis) return e;
			}
			var built = BuildEntry(host, host._sharedMesh, mirrorAxis);
			_byHost[hid] = built;
			return built;
		}

		/// <summary>
		/// If successful, <paramref name="mirroredWorld"/> is the barycentric blend of paired mesh vertices
		/// (ground-truth opposite-side point on the original triangulation), not a convex-hull point.
		/// </summary>
		public static bool TryGetMirroredWorldPoint (SD_3D_Mesh host, Vector3 worldPoint, Vector3 worldPlaneNormal, out Vector3 mirroredWorld) {
			mirroredWorld = default;
			if (host == null)
				return false;
			// Shared mesh vertices do not represent deformed runtime skin pose, so topology "ground truth"
			// would be wrong/offset on skinned meshes. Use geometric fallback there.
			if (host.GetComponent<SkinnedMeshRenderer>() != null)
				return false;
			Transform t = host.transform;
			Vector3 localPlaneN = t.InverseTransformDirection(worldPlaneNormal);
			if (localPlaneN.sqrMagnitude < 1e-8f)
				return false;
			localPlaneN.Normalize();
			if (!TryResolveAxisFromPlaneNormalLocal(localPlaneN, out MirrorAxis axis))
				return false;

			Entry e = GetOrBuild(host, axis);
			if (e == null || e.pairIndex == null) return false;
			Mesh mesh = host._sharedMesh;
			var vl = mesh.vertices;
			int[] trs = mesh.triangles;
			if (trs == null || trs.Length < 3) return false;
			int triCount = trs.Length / 3;
			int nV = vl.Length;
			Vector3 pL = t.InverseTransformPoint(worldPoint);

			const int triMaxFullSearch = 24_000;
			if (triCount <= triMaxFullSearch) {
				float best = float.MaxValue;
				int bi0 = -1, bi1 = -1, bi2 = -1;
				float bu = 0, bv = 0, bw = 0;
				for (int ti = 0; ti < triCount; ti++) {
					int i0 = trs[ti * 3 + 0], i1 = trs[ti * 3 + 1], i2 = trs[ti * 3 + 2];
					if (i0 >= nV || i1 >= nV || i2 >= nV) continue;
					var a = vl[i0];
					var b_ = vl[i1];
					var c = vl[i2];
					ClosestPointBary(pL, a, b_, c, out float u, out float v, out float w);
					var q = u * a + v * b_ + w * c;
					float d2 = (q - pL).sqrMagnitude;
					if (d2 < best) {
						best = d2;
						bi0 = i0; bi1 = i1; bi2 = i2;
						bu = u; bv = v; bw = w;
					}
				}
				if (bi0 < 0) return false;
				int p0 = e.pairIndex[bi0], p1 = e.pairIndex[bi1], p2 = e.pairIndex[bi2];
				if (p0 < 0 || p1 < 0 || p2 < 0) return false;
				// Confidence gate: if paired vertices do not approximately equal mirrored source vertices,
				// treat topology map as unreliable and fall back to geometric mirror path.
				Vector3 r0 = vl[bi0], r1 = vl[bi1], r2 = vl[bi2];
				MirrorLocalOnAxis(ref r0, axis);
				MirrorLocalOnAxis(ref r1, axis);
				MirrorLocalOnAxis(ref r2, axis);
				float pairErr = ((vl[p0] - r0).magnitude + (vl[p1] - r1).magnitude + (vl[p2] - r2).magnitude) / 3f;
				float size = mesh.bounds.size.magnitude;
				float pairErrMax = Mathf.Max(1e-5f, size * 0.0025f);
				if (pairErr > pairErrMax)
					return false;
				Vector3 ml = bu * vl[p0] + bv * vl[p1] + bw * vl[p2];
				mirroredWorld = t.TransformPoint(ml);
				return true;
			}
			// High poly: nearest vertex, pair jump (coarser)
			int bestI = 0; float bD = float.MaxValue;
			for (int i = 0; i < nV; i++) {
				float d2 = (vl[i] - pL).sqrMagnitude;
				if (d2 < bD) { bD = d2; bestI = i; }
			}
			int p = e.pairIndex[bestI];
			if (p < 0) return false;
			mirroredWorld = t.TransformPoint(vl[p]);
			return true;
		}
	}
}
