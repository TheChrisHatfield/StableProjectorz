using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Thompson-sampling calibrator for the mesh symmetry plane offset. Discrete arms = fractional offsets
	/// along the bilateral normal, relative to the model hierarchy root pivot (arm at index <c>ArmCount/2</c>
	/// is the pure root-pivot plane — the cold-start prior, matching <see cref="PaintSymmetryMesh"/>).
	///
	/// Pattern mirrors <see cref="PaintUndo_Scheduler"/>: Beta(α,β) posteriors per arm (per mesh root),
	/// <see cref="SampleBeta01"/> → argmax Thompson sample selects which candidates to evaluate each frame,
	/// Bernoulli reward from empirical mesh-surface proximity of the reflected stroke point.
	///
	/// "Bouncing" between sources of truth is intentional: the <b>theoretical</b> side is the plane-reflected
	/// stroke point, the <b>empirical</b> side is the actual mesh surface (vertex samples on readable meshes,
	/// convex-hull <c>ClosestPoint</c> otherwise). Each observation compares the two; the gap trains the
	/// posteriors so the plane walks toward the offset that best aligns the mirror with real mesh geometry.
	/// </summary>
	public static class PaintSymmetry_BanditCalibrator {

		/// <summary>Enabled by default so mirror calibration can learn online; disable from tooling when strict unlearned symmetry is needed.</summary>
		public static bool Enabled = true;

		/// <summary>
		/// Half-range of the offset search in fractions of the selection radius. Offsets span
		/// [-OffsetRangeFraction × r, +OffsetRangeFraction × r] evenly across <see cref="ArmCount"/> arms.
		/// </summary>
		public static float OffsetRangeFraction = 0.12f;

		/// <summary>Must be odd so the center arm is exactly the root-pivot plane (zero offset).</summary>
		public static int ArmCount = 9;

		/// <summary>Reflected-point distance to the mesh surface (as fraction of radius) that counts as Bernoulli success.</summary>
		public static float SuccessDistanceFraction = 0.04f;

		/// <summary>Learned offset only after at least this many <b>total</b> bandit pull updates (across all arms, see <see cref="Observe"/>).</summary>
		public static int MinPullsBeforeUse = 45;

		/// <summary>
		/// Posterior-mean advantage a non-center arm must have over the zero-offset (root pivot) arm before
		/// the plane is nudged. On many geometries the surface-proximity reward cannot discriminate between
		/// offsets (all arms succeed or all fail); without this margin the plane drifts off the true plane.
		/// </summary>
		public static float MeanAdvantageOverCenter = 0.06f;

		/// <summary>Arms explored per observation frame (Thompson-sampled without replacement).</summary>
		public static int ObservationsPerStrokeFrame = 9;

		/// <summary>Readable-mesh vertex subsample count per observation (keeps cost bounded for dense meshes).</summary>
		public static int VertexSubsampleTarget = 192;

		/// <summary>EMA over best-mean arm, to damp oscillation between adjacent arms at similar posteriors.</summary>
		public static float BestArmEmaAlpha = 0.25f;

		class MeshSampleCache {
			public SD_3D_Mesh mesh;
			public Vector3[] worldSamples;
			public Matrix4x4 capturedL2W;
			public int capturedVertCount;
		}

		class RootState {
			public double[] alpha;
			public double[] beta;
			public int[] pulls;
			public float meshRadius;
			public float emaBestArm;
			public int totalPulls;
			public readonly List<MeshSampleCache> sampleCaches = new List<MeshSampleCache>(4);
		}

		static readonly Dictionary<Transform, RootState> _byRoot = new Dictionary<Transform, RootState>();
		static bool _destroyHookWired;

		public static void EnsureDestroyHook() {
			if (_destroyHookWired) return;
			_destroyHookWired = true;
			SD_3D_Mesh.Act_OnWillDestroyMesh += OnWillDestroyMesh;
		}

		static void OnWillDestroyMesh(SD_3D_Mesh m) {
			if (m == null) return;
			Transform root = m.transform.root != null ? m.transform.root : m.transform;
			if (_byRoot.TryGetValue(root, out var s)) {
				for (int i = s.sampleCaches.Count - 1; i >= 0; i--)
					if (s.sampleCaches[i].mesh == m) s.sampleCaches.RemoveAt(i);
			}
		}

		public static void Reset (Transform rootAnchor) {
			if (rootAnchor != null && rootAnchor && _byRoot.TryGetValue(rootAnchor, out var s)) {
				for (int a = 0; a < s.alpha.Length; a++) {
					s.alpha[a] = 1;
					s.beta[a] = 1;
					s.pulls[a] = 0;
				}
				s.totalPulls = 0;
				s.emaBestArm = (ArmCount - 1) * 0.5f;
			}
		}

		static RootState EnsureState (Transform rootAnchor) {
			if (!_byRoot.TryGetValue(rootAnchor, out var s)) {
				s = new RootState {
					alpha = new double[ArmCount],
					beta = new double[ArmCount],
					pulls = new int[ArmCount],
					emaBestArm = (ArmCount - 1) * 0.5f,
				};
				for (int a = 0; a < ArmCount; a++) {
					s.alpha[a] = 1;
					s.beta[a] = 1;
				}
				_byRoot[rootAnchor] = s;
			}
			else if (s.alpha.Length != ArmCount) {
				// ArmCount was tuned at runtime: rebuild posteriors at the new size (data is not migratable
				// because arm offsets remap). Bandit restarts cold-start for this anchor.
				s.alpha = new double[ArmCount];
				s.beta = new double[ArmCount];
				s.pulls = new int[ArmCount];
				s.totalPulls = 0;
				s.emaBestArm = (ArmCount - 1) * 0.5f;
				for (int a = 0; a < ArmCount; a++) { s.alpha[a] = 1; s.beta[a] = 1; }
			}
			return s;
		}

		static bool TryEncapsulateSelectionBounds (IReadOnlyList<SD_3D_Mesh> sel, out Bounds b) {
			b = default;
			bool any = false;
			for (int i = 0; i < sel.Count; i++) {
				if (sel[i] == null) continue;
				if (!any) { b = sel[i].bounds; any = true; }
				else b.Encapsulate(sel[i].bounds);
			}
			return any;
		}

		static bool HitIsOnSelectedMesh (RaycastHit hit, IReadOnlyList<SD_3D_Mesh> sel) {
			if (hit.collider == null || sel == null) return false;
			var hitMesh = hit.collider.GetComponentInParent<SD_3D_Mesh>();
			if (hitMesh == null) return false;
			for (int i = 0; i < sel.Count; i++)
				if (sel[i] == hitMesh) return true;
			return false;
		}

		/// <summary>Arm offset in world units given a snapshot selection radius.</summary>
		public static float ArmOffset (int armIdx, float meshRadius) {
			int n = Mathf.Max(3, ArmCount);
			int mid = (n - 1) / 2;
			float t = (float)(armIdx - mid) / Mathf.Max(1, mid);
			return t * Mathf.Max(1e-4f, meshRadius) * OffsetRangeFraction;
		}

		/// <summary>
		/// Returns the currently learned offset along <paramref name="planeNormalUnit"/> for the given anchor,
		/// or zero when the posterior has not seen enough pulls to commit (cold-start).
		/// </summary>
		public static bool TryGetLearnedOffset (Transform rootAnchor, out float offsetAlongNormal) {
			offsetAlongNormal = 0f;
			if (!Enabled || rootAnchor == null || !rootAnchor) return false;
			if (ArmCount < 1) return false;
			if (!_byRoot.TryGetValue(rootAnchor, out var s)) return false;
			if (s.totalPulls < MinPullsBeforeUse) return false;
			int armIdx = Mathf.Clamp(Mathf.RoundToInt(s.emaBestArm), 0, ArmCount - 1);
			offsetAlongNormal = ArmOffset(armIdx, s.meshRadius);
			return true;
		}

		/// <summary>
		/// Consume one paint-frame hit: Thompson-sample a handful of candidate offsets, evaluate each
		/// candidate's reflected-point proximity to the mesh surface, update Beta posteriors.
		/// Safe to call every paint frame; internal reuse lists avoid per-call allocations.
		/// </summary>
		public static void Observe (RaycastHit primaryHit, Vector3 basePlanePoint, Vector3 planeNormalUnit, Transform rootAnchor) {
			if (!Enabled || rootAnchor == null || !rootAnchor) return;
			// Tuning UI could set 0; arrays and Clamp would be invalid. Public API stays flexible but guard here.
			if (ArmCount < 1) return;
			var models = ModelsHandler_3D.instance;
			if (models == null || models.selectedMeshes == null || models.selectedMeshes.Count == 0) return;
			if (planeNormalUnit.sqrMagnitude < 1e-8f) return;
			// Reflection math assumes a unit normal; planes from TryGetSymmetryPlane are normalized, but
			// a non-unit vector here would skew reflected points and poison posteriors.
			Vector3 nUnit = planeNormalUnit.normalized;
			// Only train on hits that landed on the calibration target. Hovering over an unrelated occluder
			// would otherwise feed observations with the wrong primary point and bias the posteriors.
			if (!HitIsOnSelectedMesh(primaryHit, models.selectedMeshes)) return;

			EnsureDestroyHook();

			RootState s = EnsureState(rootAnchor);

			if (!TryEncapsulateSelectionBounds(models.selectedMeshes, out Bounds b))
				return;
			float radius = Mathf.Max(0.05f, b.extents.magnitude);
			// Smooth the radius across frames so arm offsets don't jitter when bounds recompute.
			s.meshRadius = s.meshRadius <= 0f ? radius : Mathf.Lerp(s.meshRadius, radius, 0.25f);

			int explore = Mathf.Clamp(ObservationsPerStrokeFrame, 1, ArmCount);
			// Heap: stackalloc[ArmCount] is unsafe if ArmCount is tuned to a very large value at runtime.
			var chosen = new int[ArmCount];
			for (int i = 0; i < ArmCount; i++) chosen[i] = 0;

			for (int k = 0; k < explore; k++) {
				int a = ThompsonSampleExcluding(s, chosen);
				if (a < 0) break;
				chosen[a] = 1;

				float offset = ArmOffset(a, s.meshRadius);
				Vector3 planePt = basePlanePoint + nUnit * offset;
				Vector3 reflected = primaryHit.point - 2f * Vector3.Dot(primaryHit.point - planePt, nUnit) * nUnit;

				float successDist = s.meshRadius * SuccessDistanceFraction;
				bool success = ReflectedPointIsOnMeshSurface(reflected, models.selectedMeshes, s, successDist);

				s.pulls[a]++;
				s.totalPulls++;
				if (success) s.alpha[a] += 1;
				else s.beta[a] += 1;
			}

			// Best arm defaults to the center (zero-offset) arm and ties break toward it. The old argmax
			// (seeded at -1, strict '>') resolved the common all-tied posterior to arm 0 = max negative
			// offset, which shoved the mirror plane off the true symmetry plane by up to
			// OffsetRangeFraction × radius — a constant world-space paint offset that also swung around
			// with the plane normal whenever the object or view rotated.
			int mid = (ArmCount - 1) / 2;
			int bestMeanArm = mid;
			double bestMean = s.alpha[mid] / (s.alpha[mid] + s.beta[mid]);
			double midMean = bestMean;
			for (int a = 0; a < ArmCount; a++) {
				double mean = s.alpha[a] / (s.alpha[a] + s.beta[a]);
				bool strictlyBetter = mean > bestMean + 1e-9;
				bool tieCloserToCenter = mean >= bestMean - 1e-9 && Math.Abs(a - mid) < Math.Abs(bestMeanArm - mid);
				if (strictlyBetter || tieCloserToCenter) { bestMean = mean; bestMeanArm = a; }
			}
			if (bestMeanArm != mid && bestMean < midMean + MeanAdvantageOverCenter)
				bestMeanArm = mid;
			s.emaBestArm = Mathf.Lerp(s.emaBestArm, bestMeanArm, Mathf.Clamp01(BestArmEmaAlpha));
		}

		static int ThompsonSampleExcluding (RootState s, int[] excludedMask) {
			int best = -1;
			double bestSample = double.NegativeInfinity;
			for (int a = 0; a < ArmCount; a++) {
				if (excludedMask[a] != 0) continue;
				double sample = SampleBeta01(s.alpha[a], s.beta[a]);
				if (sample > bestSample) { bestSample = sample; best = a; }
			}
			return best;
		}

		/// <summary>
		/// Empirical ground truth: minimum distance from <paramref name="reflected"/> to any selected mesh's
		/// surface (readable vertex subsample if available, convex-hull <c>ClosestPoint</c> otherwise).
		/// </summary>
		static bool ReflectedPointIsOnMeshSurface (Vector3 reflected, IReadOnlyList<SD_3D_Mesh> sel, RootState s, float successDist) {
			float successSqr = successDist * successDist;
			float bestSqr = float.MaxValue;

			for (int i = 0; i < sel.Count; i++) {
				var m = sel[i];
				if (m == null) continue;

				if (m._sharedMesh != null && m._sharedMesh.isReadable) {
					var cache = GetOrBuildSampleCache(s, m);
					if (cache != null && cache.worldSamples != null) {
						var arr = cache.worldSamples;
						for (int v = 0; v < arr.Length; v++) {
							float sqr = (arr[v] - reflected).sqrMagnitude;
							if (sqr < bestSqr) bestSqr = sqr;
							if (bestSqr <= successSqr) return true;
						}
						continue;
					}
				}

				if (m._meshCollider != null) {
					Vector3 cp = m._meshCollider.ClosestPoint(reflected);
					float sqr = (cp - reflected).sqrMagnitude;
					if (sqr < bestSqr) bestSqr = sqr;
					if (bestSqr <= successSqr) return true;
				}
			}
			return bestSqr <= successSqr;
		}

		static MeshSampleCache GetOrBuildSampleCache (RootState s, SD_3D_Mesh m) {
			if (m == null || m._sharedMesh == null) return null;
			Matrix4x4 l2w = m.transform.localToWorldMatrix;
			int vertCount = m._sharedMesh.vertexCount;

			for (int i = 0; i < s.sampleCaches.Count; i++) {
				var c = s.sampleCaches[i];
				if (c.mesh != m) continue;
				if (c.capturedVertCount == vertCount && c.capturedL2W == l2w && c.worldSamples != null)
					return c;
				if (!TryRebuildWorldSamples(c, m, l2w, vertCount)) return null;
				return c;
			}

			var cache = new MeshSampleCache { mesh = m };
			if (!TryRebuildWorldSamples(cache, m, l2w, vertCount)) return null;
			s.sampleCaches.Add(cache);
			return cache;
		}

		static bool TryRebuildWorldSamples (MeshSampleCache cache, SD_3D_Mesh m, Matrix4x4 l2w, int vertCount) {
			try {
				var verts = m._sharedMesh.vertices;
				int target = Mathf.Max(16, VertexSubsampleTarget);
				int step = Mathf.Max(1, verts.Length / target);
				int count = (verts.Length + step - 1) / step;
				if (cache.worldSamples == null || cache.worldSamples.Length != count)
					cache.worldSamples = new Vector3[count];
				int j = 0;
				for (int i = 0; i < verts.Length && j < count; i += step, j++)
					cache.worldSamples[j] = l2w.MultiplyPoint3x4(verts[i]);
				cache.capturedL2W = l2w;
				cache.capturedVertCount = vertCount;
				return true;
			}
			catch {
				cache.worldSamples = null;
				return false;
			}
		}

		// --- Beta / Gamma sampling (Marsaglia-Tsang), same algorithm as PaintUndo_Scheduler ---

		static double SampleBeta01 (double alpha, double beta) {
			double x = SampleGammaMT(Math.Max(1e-6, alpha));
			double y = SampleGammaMT(Math.Max(1e-6, beta));
			return x / (x + y);
		}

		static double SampleGammaMT (double shape) {
			if (shape < 1e-9) return 0;
			if (shape < 1)
				return SampleGammaMT(1 + shape) * Math.Pow(UnityEngine.Random.value + 1e-10, 1 / shape);

			double d = shape - 1 / 3.0;
			double c = 1 / Math.Sqrt(9 * d);
			while (true) {
				double x = SampleStdNormal();
				double v = 1 + c * x;
				if (v <= 0) continue;
				v = v * v * v;
				double u = UnityEngine.Random.value;
				if (u < 1 - 0.0331 * x * x * x * x)
					return d * v;
				if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
					return d * v;
			}
		}

		static double SampleStdNormal () {
			double u1 = UnityEngine.Random.value + 1e-10;
			double u2 = UnityEngine.Random.value + 1e-10;
			return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(Math.PI * 2 * u2);
		}
	}
}
