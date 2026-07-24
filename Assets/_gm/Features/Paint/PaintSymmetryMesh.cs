using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Mesh-aware paint symmetry: mirror plane from <see cref="BrushRibbon_UI_Size"/> (auto / view / object-local / face pick)
	/// anchored on the model hierarchy root so posed / asymmetric bounds don't offset the mirror; mirrors the camera ray
	/// hit via pure plane reflection (exact for symmetric surfaces, no wrong-shell snap); falls back to screen mirror.
	/// </summary>
	public static class PaintSymmetryMesh {

		public static Vector2 ScreenMirrorViewportUV (Vector2 viewport01) =>
			new Vector2(1f - viewport01.x, viewport01.y);

		/// <summary>
		/// Bilateral plane from model-space axes at the hierarchy root. Characters and props are authored
		/// with the root pivot on the symmetry plane (local X=0); using the root position + root right
		/// gives the true symmetry plane in world space, independent of pose/bounds asymmetry. Learned
		/// offset from <see cref="PaintSymmetry_BanditCalibrator"/> is added only in
		/// <see cref="TryMirrorViewportPoint"/> / <see cref="TryMirrorViewportStroke"/> (once) so the
		/// plane and bandit <c>Observe</c> base stay consistent — never add it here or reflection doubles
		/// the offset and symmetry breaks.
		/// </summary>
		static bool TryGetObjectLocalPlane (System.Collections.Generic.IReadOnlyList<SD_3D_Mesh> sel, int objectLocalSign,
			out Vector3 planePoint, out Vector3 planeNormal) {
			planePoint = default;
			planeNormal = default;
			Vector3 rightSum = Vector3.zero;
			Vector3 posSum = Vector3.zero;
			int valid = 0;
			for (int i = 0; i < sel.Count; i++) {
				var m = sel[i];
				if (m == null)
					continue;
				Transform rt = m.transform.root != null ? m.transform.root : m.transform;
				rightSum += rt.right;
				posSum += rt.position;
				valid++;
			}
			if (valid == 0 || rightSum.sqrMagnitude < 1e-8f)
				return false;

			int latSign = objectLocalSign < 0 ? -1 : 1;
			planeNormal = (rightSum * latSign).normalized;
			planePoint = posSum / valid;
			return true;
		}

		/// <summary>
		/// Bandit state is keyed by one hierarchy root. If multiple roots are selected, the mirror plane is a
		/// blend of all mesh pivots/axes but posteriors for a single root would be inconsistent — skip nudge.
		/// </summary>
		static bool TryGetSingleRootAnchor (System.Collections.Generic.IReadOnlyList<SD_3D_Mesh> sel, out Transform anchor) {
			anchor = null;
			if (sel == null) return false;
			for (int i = 0; i < sel.Count; i++) {
				var m = sel[i];
				if (m == null) continue;
				var r = m.transform.root != null ? m.transform.root : m.transform;
				if (anchor == null)
					anchor = r;
				else if (anchor != r) {
					anchor = null;
					return false;
				}
			}
			return anchor != null;
		}

		/// <summary>User-defined face plane must not be shifted by online calibration.</summary>
		static bool SymmetryPlaneAllowsBanditNudge () {
			return true;
		}

		/// <summary>Single place calibrator offset is added to the plane (world units along <paramref name="nUnit"/>).</summary>
		static void ApplyLearnedPlaneOffset (Transform anchor, ref Vector3 planePoint, Vector3 nUnit) {
			if (anchor != null && anchor && PaintSymmetry_BanditCalibrator.TryGetLearnedOffset(anchor, out float o))
				planePoint += nUnit * o;
		}

		public static bool TryGetSymmetryPlane (Camera viewCam, out Vector3 planePoint, out Vector3 planeNormal) {
			planePoint = default;
			planeNormal = default;
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || sel.Count == 0)
				return false;

			if (!TryEncapsulateSelectionBounds(sel, out Bounds b))
				return false;
			Vector3 center = b.center;

			var sz = BrushRibbon_UI_Size.instance;
			if (sz != null) {
				switch (sz.paintSymmetryPlaneSource) {
					case PaintSymmetryPlaneSource.ViewAligned:
						if (viewCam != null) {
							planeNormal = viewCam.transform.right;
							if (planeNormal.sqrMagnitude < 1e-8f)
								return false;
							planeNormal.Normalize();
							// Vertical mirror through model pivot (not bounds center): keeps the plane on the
							// true symmetry axis even for posed characters whose world bounds are asymmetric.
							planePoint = TryGetRootPivotAverage(sel, out Vector3 pivot) ? pivot : center;
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
					case PaintSymmetryPlaneSource.ObjectLocal:
						// Bilateral axis from rig/object orientation — stable vs camera orbit (hands, limbs at oblique views).
						return TryGetObjectLocalPlane(sel, sz.symmetryObjectLocalSign, out planePoint, out planeNormal);
				}
			}

			// Auto: prefer model-local bilateral axis/origin for true opposite-side painting.
			// Fall back to view-aligned symmetry only when object-local data is unavailable.
			if (TryGetObjectLocalPlane(sel, 1, out planePoint, out planeNormal))
				return true;
			if (viewCam == null)
				return false;
			planeNormal = viewCam.transform.right;
			if (planeNormal.sqrMagnitude < 1e-8f)
				return false;
			planeNormal.Normalize();
			planePoint = TryGetRootPivotAverage(sel, out Vector3 fallbackPivot) ? fallbackPivot : center;
			return true;
		}

		static bool TryEncapsulateSelectionBounds (System.Collections.Generic.IReadOnlyList<SD_3D_Mesh> sel, out Bounds b) {
			b = default;
			bool any = false;
			for (int i = 0; i < sel.Count; i++) {
				if (sel[i] == null) continue;
				if (!any) { b = sel[i].bounds; any = true; }
				else b.Encapsulate(sel[i].bounds);
			}
			return any;
		}

		static bool TryGetRootPivotAverage (System.Collections.Generic.IReadOnlyList<SD_3D_Mesh> sel, out Vector3 avg) {
			avg = default;
			Vector3 sum = Vector3.zero;
			int n = 0;
			for (int i = 0; i < sel.Count; i++) {
				var m = sel[i];
				if (m == null)
					continue;
				Transform rt = m.transform.root != null ? m.transform.root : m.transform;
				sum += rt.position;
				n++;
			}
			if (n == 0)
				return false;
			avg = sum / n;
			return true;
		}

		public static Vector3 ReflectAcrossPlane (Vector3 point, Vector3 planePoint, Vector3 planeNormal) {
			Vector3 n = planeNormal.normalized;
			float d = Vector3.Dot(point - planePoint, n);
			return point - 2f * d * n;
		}

		/// <summary>
		/// Mirror the world-space hit point across the symmetry plane. Reflection is an affine operation,
		/// so reflecting the hit point is mathematically identical to reflecting the triangle vertices and
		/// applying the same barycentric mix; the collider ClosestPoint snap (convex-hull on MeshCollider)
		/// was the only thing that could move the result, and on asymmetric/posed meshes it routinely
		/// snapped to the wrong shell. Pure plane reflection is exact for symmetric surfaces and gracefully
		/// no-ops in air when there is no mirrored surface (brush miss rather than wrong-side paint).
		/// </summary>
		static Vector3 MirrorWorldFromHit (RaycastHit hit, Vector3 planePoint, Vector3 planeNormal) {
			return ReflectAcrossPlane(hit.point, planePoint, planeNormal);
		}

		static bool IsViewportPointVisible01 (Vector3 vp) {
			return vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
		}

		static float GetSelectionRadiusWorld () {
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || !TryEncapsulateSelectionBounds(sel, out Bounds b))
				return 1f;
			return Mathf.Max(0.05f, b.extents.magnitude);
		}

		/// <summary>
		/// Reject a mesh mirror only when source and mirrored points sit on the same side of the actual
		/// symmetry plane in world space (small dead band near the seam so on-plane strokes don't flip-flop).
		/// The previous test compared screen halves (left/right of viewport x=0.5), which wrongly rejected
		/// correct mirrors whenever the object was off screen-center or the camera orbited to an oblique view —
		/// painting then fell back to the screen-center mirror, i.e. an offset stamp.
		/// </summary>
		static bool ShouldRejectMirrorWorldPair (Vector3 sourceWorld, Vector3 mirrorWorld, Vector3 planePoint, Vector3 planeNormalUnit) {
			float deadBand = GetSelectionRadiusWorld() * 0.02f;
			float dSrc = Vector3.Dot(sourceWorld - planePoint, planeNormalUnit);
			float dMir = Vector3.Dot(mirrorWorld - planePoint, planeNormalUnit);
			return Mathf.Abs(dSrc) > deadBand && Mathf.Abs(dMir) > deadBand && dSrc * dMir > 0f;
		}

		static bool TryRaycastSelectedAtViewport (Camera cam, Vector2 viewport01, out RaycastHit selectedHit) {
			selectedHit = default;
			if (cam == null)
				return false;
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || sel.Count == 0)
				return false;

			Ray ray = cam.ViewportPointToRay(new Vector3(viewport01.x, viewport01.y, 0f));
			var hits = Physics.RaycastAll(ray, cam.farClipPlane, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
			if (hits == null || hits.Length == 0)
				return false;
			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			for (int h = 0; h < hits.Length; h++) {
				var mesh = hits[h].collider.GetComponentInParent<SD_3D_Mesh>();
				if (mesh == null)
					continue;
				for (int s = 0; s < sel.Count; s++) {
					if (sel[s] == mesh) {
						selectedHit = hits[h];
						return true;
					}
				}
			}
			return false;
		}

		static bool TryGetHitSelectedMesh (RaycastHit hit, out SD_3D_Mesh selectedMesh) {
			selectedMesh = null;
			if (hit.collider == null)
				return false;
			var mesh = hit.collider.GetComponentInParent<SD_3D_Mesh>();
			if (mesh == null)
				return false;
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null)
				return false;
			for (int i = 0; i < sel.Count; i++) {
				if (sel[i] == mesh) {
					selectedMesh = mesh;
					return true;
				}
			}
			return false;
		}

		static bool TryGetMirrorSearchRadius01 (out float radius01, out float maxSnapDistWorld) {
			radius01 = 0.0125f;
			maxSnapDistWorld = 0.08f;
			var sel = ModelsHandler_3D.instance?.selectedMeshes;
			if (sel == null || !TryEncapsulateSelectionBounds(sel, out Bounds b))
				return false;
			float r = Mathf.Max(0.05f, b.extents.magnitude);
			radius01 = Mathf.Clamp(r * 0.015f, 0.006f, 0.026f);
			maxSnapDistWorld = Mathf.Max(0.01f, r * 0.16f);
			return true;
		}

		/// <summary>
		/// Around an initial viewport candidate, probe a tiny 3x3 neighborhood and pick the selected-mesh hit
		/// whose world point is closest to <paramref name="targetWorld"/>. This stabilizes symmetry when
		/// projection alone lands between triangles or on thin silhouettes.
		/// </summary>
		static bool TryRaycastSelectedNearViewportForWorldTarget (Camera cam, Vector2 candidate01, Vector3 targetWorld, out Vector2 bestViewport01, out float bestDistSqr, out Vector3 bestHitWorld) {
			bestViewport01 = default;
			bestDistSqr = float.MaxValue;
			bestHitWorld = default;
			if (cam == null)
				return false;

			TryGetMirrorSearchRadius01(out float radius01, out _);
			for (int oy = -1; oy <= 1; oy++) {
				for (int ox = -1; ox <= 1; ox++) {
					Vector2 p = candidate01 + new Vector2(ox, oy) * radius01;
					p.x = Mathf.Clamp01(p.x);
					p.y = Mathf.Clamp01(p.y);
					if (!TryRaycastSelectedAtViewport(cam, p, out RaycastHit hit))
						continue;
					float d2 = (hit.point - targetWorld).sqrMagnitude;
					if (d2 < bestDistSqr) {
						bestDistSqr = d2;
						bestViewport01 = p;
						bestHitWorld = hit.point;
					}
				}
			}
			return bestDistSqr < float.MaxValue;
		}

		/// <summary>
		/// Foundational mesh mapping from a world-space target:
		/// 1) project target world to viewport,
		/// 3) cast that viewport ray back onto selected mesh (ground truth),
		/// 4) probe a small neighborhood and accept only if re-hit is spatially close to target.
		/// This prevents offset/same-side artifacts from using projected reflection alone.
		/// </summary>
		static bool TryMeshMirrorViewportFromWorldTarget (Camera cam, Vector3 targetWorld, out Vector2 mirrorViewport01, out Vector3 mirrorHitWorld) {
			mirrorViewport01 = default;
			mirrorHitWorld = default;
			if (cam == null)
				return false;
			Vector3 vp = cam.WorldToViewportPoint(targetWorld);
			if (!IsViewportPointVisible01(vp))
				return false;
			Vector2 candidate01 = new Vector2(vp.x, vp.y);
			if (!TryRaycastSelectedNearViewportForWorldTarget(cam, candidate01, targetWorld, out Vector2 bestViewport, out float bestDistSqr, out Vector3 bestHitWorld))
				return false;
			TryGetMirrorSearchRadius01(out _, out float maxSnapDist);
			if (bestDistSqr > maxSnapDist * maxSnapDist)
				return false;
			mirrorViewport01 = bestViewport;
			mirrorHitWorld = bestHitWorld;
			return true;
		}

		/// <summary>
		/// Ground-truth bilateral: closest triangle on the readable render mesh, bary on paired opposite vertices, world target → viewport. No re-ray; avoids convex hull / wrong-triangle issues.
		/// </summary>
		static bool TryMeshMirrorViewportFromTopology (Camera cam, RaycastHit sourceHit, Vector3 symmetryPlaneNormal, out Vector2 mirrorViewport01) {
			mirrorViewport01 = default;
			if (cam == null) return false;
			if (!TryGetHitSelectedMesh(sourceHit, out SD_3D_Mesh m)) return false;
			if (!MeshSymmetryTopologyCache.TryGetMirroredWorldPoint(m, sourceHit.point, symmetryPlaneNormal, out Vector3 wMir)) return false;
			var vp = cam.WorldToViewportPoint(wMir);
			if (!IsViewportPointVisible01(vp)) return false;
			mirrorViewport01 = new Vector2(vp.x, vp.y);
			// No same-lateral screen rejection: topology is the opposite mesh point; oblique views can project it on the “same” viewport half.
			return true;
		}

		/// <summary>
		/// Model-space bilateral mirror (DCC-style): mirror hit in root local X and re-hit selected mesh.
		/// This is the primary path for character-style assets authored around local X=0.
		/// </summary>
		static bool TryMeshMirrorViewportFromLocalRoot (Camera cam, RaycastHit sourceHit, out Vector2 mirrorViewport01, out Vector3 mirrorHitWorld) {
			mirrorViewport01 = default;
			mirrorHitWorld = default;
			if (!TryGetHitSelectedMesh(sourceHit, out SD_3D_Mesh hitMesh))
				return false;
			Transform root = hitMesh.transform.root != null ? hitMesh.transform.root : hitMesh.transform;
			Vector3 pLocal = root.InverseTransformPoint(sourceHit.point);
			pLocal.x = -pLocal.x;
			Vector3 targetWorld = root.TransformPoint(pLocal);
			return TryMeshMirrorViewportFromWorldTarget(cam, targetWorld, out mirrorViewport01, out mirrorHitWorld);
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

		public static bool TryMirrorViewportPoint (Camera cam, Vector2 viewport01, out Vector2 mirrorViewport01, bool allowMeshSymmetry = true) =>
			TryMirrorViewportPoint(cam, viewport01, out mirrorViewport01, allowMeshSymmetry, out _);

		/// <param name="sourceOnSelectedMesh">True when the source viewport ray hit a selected mesh. Lets callers
		/// suppress the mirrored twin (instead of painting an offset screen-center mirror) when the mesh mirror
		/// could not be resolved while the user is actually painting on the mesh.</param>
		public static bool TryMirrorViewportPoint (Camera cam, Vector2 viewport01, out Vector2 mirrorViewport01, bool allowMeshSymmetry, out bool sourceOnSelectedMesh) {
			mirrorViewport01 = ScreenMirrorViewportUV(viewport01);
			sourceOnSelectedMesh = false;
			if (!allowMeshSymmetry || cam == null || !TryGetSymmetryPlane(cam, out Vector3 c, out Vector3 n))
				return false;
			if (!TryPreferredRaycast(cam, viewport01, out RaycastHit hit))
				return false;
			sourceOnSelectedMesh = TryGetHitSelectedMesh(hit, out _);

			// c,n = unlearned symmetry plane. Observe uses that base (arms are absolute offsets in world
			// space). Then apply at most one learned nudge for the actual reflection.
			if (SymmetryPlaneAllowsBanditNudge() && TryGetSingleRootAnchor(ModelsHandler_3D.instance?.selectedMeshes, out Transform anchor)) {
				PaintSymmetry_BanditCalibrator.Observe(hit, c, n, anchor);
				ApplyLearnedPlaneOffset(anchor, ref c, n);
			}

			// Mesh triangulation + paired-vertex map (ground truth when cache builds).
			if (TryMeshMirrorViewportFromTopology(cam, hit, n, out Vector2 topoMir)) {
				mirrorViewport01 = topoMir;
				return true;
			}

			// Root-local mirror + selected-mesh re-hit.
			if (TryMeshMirrorViewportFromLocalRoot(cam, hit, out Vector2 localMir, out Vector3 localHitWorld)
			    && !ShouldRejectMirrorWorldPair(hit.point, localHitWorld, c, n)) {
				mirrorViewport01 = localMir;
				return true;
			}

			// Fallback: geometric plane reflection + selected-mesh re-hit.
			Vector3 reflected = MirrorWorldFromHit(hit, c, n);
			if (!TryMeshMirrorViewportFromWorldTarget(cam, reflected, out Vector2 meshMir, out Vector3 meshMirHitWorld))
				return false;
			if (ShouldRejectMirrorWorldPair(hit.point, meshMirHitWorld, c, n))
				return false;
			mirrorViewport01 = meshMir;
			return true;
		}

		public static bool TryMirrorViewportStroke (Camera cam, Vector2 prevViewport01, Vector2 newViewport01,
			out Vector2 mirrorPrev01, out Vector2 mirrorNew01, bool allowMeshSymmetry = true) =>
			TryMirrorViewportStroke(cam, prevViewport01, newViewport01, out mirrorPrev01, out mirrorNew01, allowMeshSymmetry, out _);

		/// <param name="sourceOnSelectedMesh">True when the current stroke endpoint ray hit a selected mesh (see
		/// <see cref="TryMirrorViewportPoint(Camera,Vector2,out Vector2,bool,out bool)"/>).</param>
		public static bool TryMirrorViewportStroke (Camera cam, Vector2 prevViewport01, Vector2 newViewport01,
			out Vector2 mirrorPrev01, out Vector2 mirrorNew01, bool allowMeshSymmetry, out bool sourceOnSelectedMesh) {
			mirrorPrev01 = ScreenMirrorViewportUV(prevViewport01);
			mirrorNew01 = ScreenMirrorViewportUV(newViewport01);
			sourceOnSelectedMesh = false;
			if (!allowMeshSymmetry || cam == null || !TryGetSymmetryPlane(cam, out Vector3 c, out Vector3 n))
				return false;
			if (!TryPreferredRaycast(cam, prevViewport01, out RaycastHit hitPrev))
				return false;
			if (!TryPreferredRaycast(cam, newViewport01, out RaycastHit hitNew))
				return false;
			sourceOnSelectedMesh = TryGetHitSelectedMesh(hitNew, out _);

			if (SymmetryPlaneAllowsBanditNudge() && TryGetSingleRootAnchor(ModelsHandler_3D.instance?.selectedMeshes, out Transform anchor)) {
				PaintSymmetry_BanditCalibrator.Observe(hitNew, c, n, anchor);
				ApplyLearnedPlaneOffset(anchor, ref c, n);
			}

			if (TryMeshMirrorViewportFromTopology(cam, hitPrev, n, out Vector2 pTopo)
			    && TryMeshMirrorViewportFromTopology(cam, hitNew, n, out Vector2 nTopo)) {
				mirrorPrev01 = pTopo;
				mirrorNew01 = nTopo;
				return true;
			}

			if (TryMeshMirrorViewportFromLocalRoot(cam, hitPrev, out Vector2 pMirLocal, out Vector3 pMirLocalWorld)
			    && TryMeshMirrorViewportFromLocalRoot(cam, hitNew, out Vector2 nMirLocal, out Vector3 nMirLocalWorld)
			    && !ShouldRejectMirrorWorldPair(hitPrev.point, pMirLocalWorld, c, n)
			    && !ShouldRejectMirrorWorldPair(hitNew.point, nMirLocalWorld, c, n)) {
				mirrorPrev01 = pMirLocal;
				mirrorNew01 = nMirLocal;
				return true;
			}

			Vector3 rPrev = MirrorWorldFromHit(hitPrev, c, n);
			Vector3 rNew = MirrorWorldFromHit(hitNew, c, n);
			if (!TryMeshMirrorViewportFromWorldTarget(cam, rPrev, out Vector2 pMir, out Vector3 pMirHitWorld)
			    || !TryMeshMirrorViewportFromWorldTarget(cam, rNew, out Vector2 nMir, out Vector3 nMirHitWorld))
				return false;
			if (ShouldRejectMirrorWorldPair(hitPrev.point, pMirHitWorld, c, n) || ShouldRejectMirrorWorldPair(hitNew.point, nMirHitWorld, c, n))
				return false;
			mirrorPrev01 = pMir;
			mirrorNew01 = nMir;
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
		/// mode 0=off, 1=screen mirror, 3=object mirror (world plane uniforms; the shader reflects each
		/// FRAGMENT's world position across the plane and evaluates the original stroke there — exact
		/// bilateral symmetry). Mode 2 (mirrored stroke coordinates from raycast re-projection) is retired:
		/// plane-estimate, probe re-hit and perspective errors accumulated into visibly offset twins.
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

			if (allowMeshSymmetry) {
				if (TryGetSymmetryPlane(cam, out Vector3 planePoint, out Vector3 planeNormal)) {
					mat.SetFloat(modeProp, 3f);
					mat.SetVector("_SymmetryPlanePointWS", planePoint);
					mat.SetVector("_SymmetryPlaneNormalWS", planeNormal);
					mat.SetVector(mirrorProp, Vector4.zero);
					mat.SetFloat(angleDeltaProp, 0f);
					return;
				}
				// Mesh context but no resolvable plane: suppress the twin. Falling back to the screen
				// mirror would place an offset ring/stamp on the mesh (the artifact mode 3 eliminates).
				mat.SetFloat(modeProp, 0f);
				mat.SetVector(mirrorProp, Vector4.zero);
				mat.SetFloat(angleDeltaProp, 0f);
				return;
			}

			mat.SetFloat(modeProp, 1f);
			mat.SetVector(mirrorProp, Vector4.zero);
			mat.SetFloat(angleDeltaProp, ComputeScreenMirrorAngleDelta(prevViewport01, newViewport01));
		}
	}
}
