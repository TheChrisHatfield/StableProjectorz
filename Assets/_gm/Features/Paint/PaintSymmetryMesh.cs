using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Mesh-aware paint symmetry: mirror plane from <see cref="BrushRibbon_UI_Size"/> (auto / view-aligned / face pick),
	/// raycast hit, then triangle-vertex correspondence when readable <see cref="MeshCollider"/>; viewport refine + screen mirror fallbacks.
	/// </summary>
	public static class PaintSymmetryMesh {

		public static Vector2 ScreenMirrorViewportUV (Vector2 viewport01) =>
			new Vector2(1f - viewport01.x, viewport01.y);

		public static bool TryGetSymmetryPlane (Camera viewCam, out Vector3 planePoint, out Vector3 planeNormal) {
			planePoint = default;
			planeNormal = default;
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || sel.Count == 0)
				return false;

			Bounds b = sel[0].bounds;
			for (int i = 1; i < sel.Count; i++)
				b.Encapsulate(sel[i].bounds);
			Vector3 center = b.center;

			var sz = BrushRibbon_UI_Size.instance;
			if (sz != null) {
				switch (sz.paintSymmetryPlaneSource) {
					case PaintSymmetryPlaneSource.ViewAligned:
						if (viewCam != null) {
							planePoint = center;
							planeNormal = viewCam.transform.right;
							if (planeNormal.sqrMagnitude < 1e-8f)
								return false;
							planeNormal.Normalize();
							return true;
						}
						break;
					case PaintSymmetryPlaneSource.FacePick:
						planePoint = sz.symmetryPlanePointWorld;
						planeNormal = sz.symmetryPlaneNormalWorld;
						if (planeNormal.sqrMagnitude < 1e-8f)
							return false;
						planeNormal.Normalize();
						return true;
				}
			}

			planePoint = center;
			Vector3 sumRight = Vector3.zero;
			for (int i = 0; i < sel.Count; i++) {
				if (sel[i] != null)
					sumRight += sel[i].transform.right;
			}
			planeNormal = sumRight.normalized;
			return planeNormal.sqrMagnitude > 1e-8f;
		}

		public static Vector3 ReflectAcrossPlane (Vector3 point, Vector3 planePoint, Vector3 planeNormal) {
			Vector3 n = planeNormal.normalized;
			float d = Vector3.Dot(point - planePoint, n);
			return point - 2f * d * n;
		}

		/// <summary>Barycentric coordinates of <paramref name="p"/> on triangle a,b,c (same plane).</summary>
		static Vector3 BarycentricOnTriangle (Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
			Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
			float d00 = Vector3.Dot(v0, v0);
			float d01 = Vector3.Dot(v0, v1);
			float d11 = Vector3.Dot(v1, v1);
			float d20 = Vector3.Dot(v2, v0);
			float d21 = Vector3.Dot(v2, v1);
			float denom = d00 * d11 - d01 * d01;
			if (Mathf.Abs(denom) < 1e-16f)
				return new Vector3(1f / 3f, 1f / 3f, 1f / 3f);
			float v = (d11 * d20 - d01 * d21) / denom;
			float w = (d00 * d21 - d01 * d20) / denom;
			float u = 1f - v - w;
			return new Vector3(u, v, w);
		}

		/// <summary>
		/// Mirror world target from hit: reflect the three triangle vertices, apply the same barycentric mix as
		/// <paramref name="hit"/>.point on the original face, then snap to the collider surface.
		/// </summary>
		static bool TryMirrorWorldViaHitTriangle (RaycastHit hit, Vector3 planePoint, Vector3 planeNormal, out Vector3 mirroredOnSurfaceWorld) {
			mirroredOnSurfaceWorld = default;
			if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null)
				return false;
			int tri = hit.triangleIndex;
			if (tri < 0)
				return false;
			Mesh mesh = mc.sharedMesh;
			if (!mesh.isReadable)
				return false;
			var tris = mesh.triangles;
			int t3 = tri * 3;
			if (t3 + 2 >= tris.Length)
				return false;
			int i0 = tris[t3];
			int i1 = tris[t3 + 1];
			int i2 = tris[t3 + 2];
			var verts = mesh.vertices;
			if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
				return false;

			Transform xf = hit.collider.transform;
			Vector3 w0 = xf.TransformPoint(verts[i0]);
			Vector3 w1 = xf.TransformPoint(verts[i1]);
			Vector3 w2 = xf.TransformPoint(verts[i2]);
			Vector3 bc = BarycentricOnTriangle(hit.point, w0, w1, w2);

			Vector3 r0 = ReflectAcrossPlane(w0, planePoint, planeNormal);
			Vector3 r1 = ReflectAcrossPlane(w1, planePoint, planeNormal);
			Vector3 r2 = ReflectAcrossPlane(w2, planePoint, planeNormal);
			Vector3 candidate = bc.x * r0 + bc.y * r1 + bc.z * r2;
			mirroredOnSurfaceWorld = hit.collider.ClosestPoint(candidate);
			return true;
		}

		/// <summary>Vertex-triangle correspondence when possible; otherwise plane-reflect hit point.</summary>
		static Vector3 MirrorWorldFromHit (RaycastHit hit, Vector3 planePoint, Vector3 planeNormal) {
			if (TryMirrorWorldViaHitTriangle(hit, planePoint, planeNormal, out Vector3 onSurface))
				return onSurface;
			return ReflectAcrossPlane(hit.point, planePoint, planeNormal);
		}

		/// <summary>Prefer closest hit on selected meshes; otherwise closest hit on anything.</summary>
		public static bool TryPreferredRaycast (Camera cam, Vector2 viewport01, out RaycastHit bestHit) {
			bestHit = default;
			if (cam == null)
				return false;

			Ray ray = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));
			var hits = Physics.RaycastAll(ray, cam.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			if (hits == null || hits.Length == 0)
				return false;

			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel != null && sel.Count > 0) {
				for (int h = 0; h < hits.Length; h++) {
					var mesh = hits[h].collider.GetComponentInParent<SD_3D_Mesh>();
					if (mesh == null)
						continue;
					for (int s = 0; s < sel.Count; s++) {
						if (sel[s] == mesh) {
							bestHit = hits[h];
							return true;
						}
					}
				}
			}

			bestHit = hits[0];
			return true;
		}

		static bool IsMeshInSelection (SD_3D_Mesh mesh, System.Collections.Generic.IReadOnlyList<SD_3D_Mesh> sel) {
			if (mesh == null || sel == null) return false;
			for (int s = 0; s < sel.Count; s++)
				if (sel[s] == mesh) return true;
			return false;
		}

		/// <summary>
		/// Ground-truth reinforcement: snap mirror UV to the selected-surface hit closest to reflected world point.
		/// If no selected hit exists, UV is unchanged (caller still has a valid fallback UV).
		/// </summary>
		static void RefineMirrorUvOnSelectedSurface (Camera cam, Vector3 reflectedWorldHint, ref Vector2 mirrorUv01) {
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || sel.Count == 0 || cam == null)
				return;

			Ray ray = cam.ViewportPointToRay(new Vector3(mirrorUv01.x, mirrorUv01.y, 0f));
			var hits = Physics.RaycastAll(ray, cam.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			if (hits == null || hits.Length == 0)
				return;

			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

			int bestIx = -1;
			float bestScore = float.MaxValue;
			for (int i = 0; i < hits.Length; i++) {
				var mesh = hits[i].collider.GetComponentInParent<SD_3D_Mesh>();
				if (!IsMeshInSelection(mesh, sel))
					continue;
				float d = (hits[i].point - reflectedWorldHint).sqrMagnitude;
				if (d < bestScore) {
					bestScore = d;
					bestIx = i;
				}
			}

			if (bestIx < 0)
				return;

			Vector3 vp = cam.WorldToViewportPoint(hits[bestIx].point);
			if (vp.z <= 0f)
				return;
			mirrorUv01 = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
		}

		public static bool TryMirrorViewportPoint (Camera cam, Vector2 viewport01, out Vector2 mirrorViewport01, bool allowMeshSymmetry = true) {
			mirrorViewport01 = ScreenMirrorViewportUV(viewport01);
			if (!allowMeshSymmetry || cam == null || !TryGetSymmetryPlane(cam, out Vector3 c, out Vector3 n))
				return false;
			if (!TryPreferredRaycast(cam, viewport01, out RaycastHit hit))
				return false;

			Vector3 reflected = MirrorWorldFromHit(hit, c, n);
			Vector3 vp = cam.WorldToViewportPoint(reflected);
			if (vp.z <= 0f)
				return false;

			Vector2 uv = new Vector2(vp.x, vp.y);
			RefineMirrorUvOnSelectedSurface(cam, reflected, ref uv);
			mirrorViewport01 = uv;
			return true;
		}

		public static bool TryMirrorViewportStroke (Camera cam, Vector2 prevViewport01, Vector2 newViewport01,
			out Vector2 mirrorPrev01, out Vector2 mirrorNew01, bool allowMeshSymmetry = true) {
			mirrorPrev01 = ScreenMirrorViewportUV(prevViewport01);
			mirrorNew01 = ScreenMirrorViewportUV(newViewport01);
			if (!allowMeshSymmetry || cam == null || !TryGetSymmetryPlane(cam, out Vector3 c, out Vector3 n))
				return false;
			if (!TryPreferredRaycast(cam, prevViewport01, out RaycastHit hitPrev))
				return false;
			if (!TryPreferredRaycast(cam, newViewport01, out RaycastHit hitNew))
				return false;

			Vector3 rPrev = MirrorWorldFromHit(hitPrev, c, n);
			Vector3 rNew = MirrorWorldFromHit(hitNew, c, n);
			Vector3 vpP = cam.WorldToViewportPoint(rPrev);
			Vector3 vpN = cam.WorldToViewportPoint(rNew);
			if (vpP.z <= 0f || vpN.z <= 0f)
				return false;

			Vector2 mp = new Vector2(vpP.x, vpP.y);
			Vector2 mn = new Vector2(vpN.x, vpN.y);
			RefineMirrorUvOnSelectedSurface(cam, rPrev, ref mp);
			RefineMirrorUvOnSelectedSurface(cam, rNew, ref mn);
			mirrorPrev01 = mp;
			mirrorNew01 = mn;
			return true;
		}

		public static float ComputeMirrorStrokeAngleDelta (Vector2 prevViewport01, Vector2 newViewport01, Vector2 mirrorPrev01, Vector2 mirrorNew01) {
			Vector2 v = newViewport01 - prevViewport01;
			Vector2 mv = mirrorNew01 - mirrorPrev01;
			if (v.sqrMagnitude < 1e-12f || mv.sqrMagnitude < 1e-12f)
				return 0f;
			float a = Mathf.Atan2(v.y, v.x);
			float ma = Mathf.Atan2(mv.y, mv.x);
			return Mathf.DeltaAngle(a * Mathf.Rad2Deg, ma * Mathf.Rad2Deg) * Mathf.Deg2Rad;
		}

		public static float ComputeScreenMirrorAngleDelta (Vector2 prevViewport01, Vector2 newViewport01) {
			Vector2 mp = ScreenMirrorViewportUV(prevViewport01);
			Vector2 mn = ScreenMirrorViewportUV(newViewport01);
			return ComputeMirrorStrokeAngleDelta(prevViewport01, newViewport01, mp, mn);
		}

		/// <summary>
		/// Sets symmetry uniforms for brushes/projection cursor.
		/// mode 0=off, 1=screen mirror, 2=mesh mirror (explicit mirrored stroke coordinates).
		/// Also sets _SymmetryMirrorAngleDeltaRad so directional tips align on mirrored strokes.
		/// </summary>
		public static void SetMaterialSymmetry (Material mat, Camera cam, Vector2 prevViewport01, Vector2 newViewport01,
			bool symmetryEnabled, bool allowMeshSymmetry) {
			const string modeProp = "_SymmetryMode";
			const string mirrorProp = "_MirrorPrevNewBrushScreenCoord";
			const string angleDeltaProp = "_SymmetryMirrorAngleDeltaRad";

			if (!symmetryEnabled) {
				mat.SetFloat(modeProp, 0f);
				mat.SetVector(mirrorProp, Vector4.zero);
				mat.SetFloat(angleDeltaProp, 0f);
				return;
			}

			if (allowMeshSymmetry && cam != null
			    && TryMirrorViewportStroke(cam, prevViewport01, newViewport01, out Vector2 mp, out Vector2 mn, true)) {
				mat.SetVector(mirrorProp, new Vector4(mp.x, mp.y, mn.x, mn.y));
				mat.SetFloat(modeProp, 2f);
				mat.SetFloat(angleDeltaProp, ComputeMirrorStrokeAngleDelta(prevViewport01, newViewport01, mp, mn));
				return;
			}

			mat.SetFloat(modeProp, 1f);
			mat.SetVector(mirrorProp, Vector4.zero);
			mat.SetFloat(angleDeltaProp, ComputeScreenMirrorAngleDelta(prevViewport01, newViewport01));
		}
	}
}
