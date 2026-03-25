using System;
using UnityEngine;

namespace spz {

	public enum RestoreBudgetPolicy {
		FixedMiddle,
		Ucb1,
		Thompson
	}

	/// <summary>Per-frame restore budget: EWMA hitch proxy, LAVD-style aging, UCB1 or Thompson over discrete budget arms.
	/// <see cref="BeginRestoreSession"/> scales caps from resolution × slice count (4K / many UDIMs → fewer slices per frame, wider time budget).</summary>
	public class PaintUndo_Scheduler {

		public float baseBudgetMs = 2.5f;
		public float minBudgetMs = 0.75f;
		public float maxBudgetMs = 8f;
		public int minSlicesPerFrame = 1;
		public int maxSlicesPerFrame = 8;
		public float agingBoostPerSecond = 0.35f;
		public float agingMaxMultiplier = 4f;

		/// <summary>Restore arm selection. Replaces legacy <c>useUcbBudgetSelection</c> (Ucb1 vs fixed middle).</summary>
		public RestoreBudgetPolicy restoreBudgetPolicy = RestoreBudgetPolicy.Thompson;

		/// <summary>Bernoulli success when <see cref="RegisterRestoreBanditObservation"/> uses Thompson: hitch below this (ms) and at least one slice uploaded.</summary>
		public float restoreThompsonSuccessHitchMs = 8f;

		/// <summary>Per new restore session, posteriors decay toward Beta(1,1) (all buckets). 0 = no decay, 1 = full reset to prior.</summary>
		[Range(0f, 1f)]
		public float restorePosteriorDecayPerSession = 0.08f;

		public int restoreContextBucketCount = 8;

		/// <summary>Reference pixel count (~one 512² UDIM). Loads above this tighten slice batching.</summary>
		public float referencePixelsPerSlice = 512f * 512f;

		/// <summary>Enable Thompson/UCB over discrete capture readback/yield arms (cold-start uses legacy <see cref="GetCaptureGpuReadbackMaxInflight"/> / <see cref="GetCapturePostReadbackYieldFrames"/>).</summary>
		public bool captureBanditEnabled = true;

		public int captureBanditMinPullsPerBucket = 3;

		/// <summary>Frames after readback+yields to measure hitch for capture bandit observation.</summary>
		public int captureObserveFrames = 3;

		/// <summary>Hitch threshold (ms, max over window) for capture Bernoulli success.</summary>
		public float captureSuccessMaxHitchMs = 12f;

		float _ewmaHitchMs = 0f;
		float _ewmaAlpha = 0.12f;
		float _restoreStartedRealtime;
		int _totalPullsUcb;
		readonly double[] _ucbRewards = new double[RestoreArmCount];
		readonly int[] _ucbPulls = new int[RestoreArmCount];
		const int RestoreArmCount = 3;
		static readonly float[] ArmBudgetMul = { 0.6f, 1f, 1.5f };
		int _lastArm = 1;

		int _restoreContextBucket;
		double[,] _restoreThompsonAlpha;
		double[,] _restoreThompsonBeta;

		int _captureContextBucket;
		int _lastCaptureArm = -1;
		const int CaptureArmCount = 6;
		int[] _captureJobsSeenPerBucket;
		int[,] _capturePulls;
		double[,] _captureThompsonAlpha;
		double[,] _captureThompsonBeta;

		void EnsureRestoreBucketArrays() {
			int b = Mathf.Max(2, restoreContextBucketCount);
			if (_restoreThompsonAlpha != null && _restoreThompsonAlpha.GetLength(0) == b) return;
			_restoreThompsonAlpha = new double[b, RestoreArmCount];
			_restoreThompsonBeta = new double[b, RestoreArmCount];
			for (int i = 0; i < b; i++)
				for (int a = 0; a < RestoreArmCount; a++) {
					_restoreThompsonAlpha[i, a] = 1;
					_restoreThompsonBeta[i, a] = 1;
				}
		}

		void EnsureCaptureBucketArrays() {
			int b = Mathf.Max(2, restoreContextBucketCount);
			if (_capturePulls != null && _capturePulls.GetLength(0) == b) return;
			_captureJobsSeenPerBucket = new int[b];
			_capturePulls = new int[b, CaptureArmCount];
			_captureThompsonAlpha = new double[b, CaptureArmCount];
			_captureThompsonBeta = new double[b, CaptureArmCount];
			for (int i = 0; i < b; i++) {
				_captureJobsSeenPerBucket[i] = 0;
				for (int a = 0; a < CaptureArmCount; a++) {
					_capturePulls[i, a] = 0;
					_captureThompsonAlpha[i, a] = 1;
					_captureThompsonBeta[i, a] = 1;
				}
			}
		}

		public static int QuantizeContextBucket(float complexity01, int sliceCount, int bucketCount) {
			int bc = Mathf.Max(2, bucketCount);
			int c = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(complexity01) * bc), 0, bc - 1);
			int tier = sliceCount <= 1 ? 0 : sliceCount <= 4 ? 1 : 2;
			// Mix slice tier into bucket without exploding count: offset by tier bands
			int mixed = (c + tier * bc / 3) % bc;
			return mixed;
		}

		/// <summary>Last session: total pixels (w×h×slices) and normalized complexity in [0,1] for logging/UI.</summary>
		public long LastSessionTotalPixels { get; private set; }
		public float LastSessionComplexity01 { get; private set; }

		float _sessionSliceCapMul = 1f;
		float _sessionBaseBudgetMul = 1f;
		float _sessionMaxBudgetMul = 1f;

		public void ResetSession() => BeginRestoreSession(512, 512, 1);

		/// <summary>Shared workload metric (capture + restore). <paramref name="totalLoad"/> is (per-slice load vs reference) × slice count.</summary>
		public static void EvaluateWorkload(int width, int height, int sliceCount, float referencePixelsPerSlice,
			out long totalPixels, out float complexity01, out float totalLoad) {
			totalPixels = 0;
			complexity01 = 0f;
			totalLoad = 0f;
			if (width <= 0 || height <= 0 || sliceCount <= 0) return;
			long perSlice = (long)width * height;
			totalPixels = perSlice * sliceCount;
			float refPx = Mathf.Max(1f, referencePixelsPerSlice);
			float load = (float)((double)perSlice / refPx);
			totalLoad = load * sliceCount;
			complexity01 = Mathf.Clamp01(Mathf.Log10(totalLoad + 1f) / 3.2f);
		}

		/// <summary>Max concurrent AsyncGPUReadback requests during undo capture. Returns 0 = use fully parallel path (all slices at once).</summary>
		public static int GetCaptureGpuReadbackMaxInflight(float complexity01, int sliceCount) {
			if (sliceCount <= 1) return 0;
			if (complexity01 < 0.2f) return 0;
			if (complexity01 < 0.45f) return Mathf.Clamp(Mathf.Max(2, sliceCount / 2), 2, 6);
			return Mathf.Clamp(Mathf.Max(1, sliceCount / 4), 1, 3);
		}

		/// <summary>Extra end-of-frame yields after all readbacks complete (spreads Texture2D alloc/Apply before blob build).</summary>
		public static int GetCapturePostReadbackYieldFrames(float complexity01) {
			return complexity01 < 0.15f ? 0 : Mathf.Clamp(Mathf.RoundToInt(complexity01 * 3f), 1, 3);
		}

		static void ResolveCaptureArmParameters(int armId, float complexity01, int sliceCount, out int maxInflight, out int postYields) {
			int L = GetCaptureGpuReadbackMaxInflight(complexity01, sliceCount);
			int Y = GetCapturePostReadbackYieldFrames(complexity01);
			switch (Mathf.Clamp(armId, 0, CaptureArmCount - 1)) {
				case 0:
					maxInflight = L;
					postYields = Y;
					break;
				case 1:
					maxInflight = 0;
					postYields = Y;
					break;
				case 2:
					maxInflight = L;
					postYields = 0;
					break;
				case 3:
					maxInflight = L;
					postYields = Mathf.Clamp(Y + 1, 0, 4);
					break;
				case 4: {
					int tight = sliceCount <= 1 ? 0 : Mathf.Clamp(Mathf.Max(1, sliceCount / 3), 1, 8);
					maxInflight = L <= 0 ? tight : Mathf.Max(L, tight);
					postYields = Y;
					break;
				}
				case 5:
					maxInflight = 0;
					postYields = 0;
					break;
				default:
					maxInflight = L;
					postYields = Y;
					break;
			}
			if (maxInflight >= sliceCount) maxInflight = 0;
		}

		/// <summary>Select capture readback/yield arm; returns arm index (for logging/observation). Uses legacy parameters when bandit disabled or cold-start.</summary>
		public int SelectCaptureArm(float complexity01, int sliceCount, out int maxInflight, out int postYields) {
			EnsureCaptureBucketArrays();
			_captureContextBucket = QuantizeContextBucket(complexity01, sliceCount, restoreContextBucketCount);
			int b = _captureContextBucket;
			_captureJobsSeenPerBucket[b]++;

			bool coldStart = captureBanditMinPullsPerBucket > 0
			                 && _captureJobsSeenPerBucket[b] <= captureBanditMinPullsPerBucket;

			if (!captureBanditEnabled || coldStart) {
				_lastCaptureArm = -1;
				maxInflight = GetCaptureGpuReadbackMaxInflight(complexity01, sliceCount);
				postYields = GetCapturePostReadbackYieldFrames(complexity01);
				if (maxInflight >= sliceCount) maxInflight = 0;
				return -1;
			}

			int best = 0;
			double bestSample = double.NegativeInfinity;
			for (int a = 0; a < CaptureArmCount; a++) {
				double s = SampleBeta01(_captureThompsonAlpha[b, a], _captureThompsonBeta[b, a]);
				if (s > bestSample) {
					bestSample = s;
					best = a;
				}
			}
			_lastCaptureArm = best;
			ResolveCaptureArmParameters(best, complexity01, sliceCount, out maxInflight, out postYields);
			return best;
		}

		public void RegisterCaptureBanditObservation(bool success) {
			if (!captureBanditEnabled || _lastCaptureArm < 0) return;
			EnsureCaptureBucketArrays();
			int b = _captureContextBucket;
			int a = Mathf.Clamp(_lastCaptureArm, 0, CaptureArmCount - 1);
			_capturePulls[b, a]++;
			if (success) _captureThompsonAlpha[b, a] += 1;
			else _captureThompsonBeta[b, a] += 1;
		}

		void DecayRestorePosteriorsTowardUniform() {
			EnsureRestoreBucketArrays();
			float d = Mathf.Clamp01(restorePosteriorDecayPerSession);
			if (d <= 0f) return;
			int b0 = _restoreThompsonAlpha.GetLength(0);
			for (int i = 0; i < b0; i++)
				for (int a = 0; a < RestoreArmCount; a++) {
					_restoreThompsonAlpha[i, a] = _restoreThompsonAlpha[i, a] * (1 - d) + d * 1;
					_restoreThompsonBeta[i, a] = _restoreThompsonBeta[i, a] * (1 - d) + d * 1;
				}
		}

		/// <summary>Start a new restore: reset bandit state, EWMA, and derive batch/budget multipliers from workload (no user tuning).</summary>
		public void BeginRestoreSession(int width, int height, int sliceCount) {
			_restoreStartedRealtime = Time.realtimeSinceStartup;
			_ewmaHitchMs = 0f;
			_totalPullsUcb = 0;
			for (int i = 0; i < RestoreArmCount; i++) {
				_ucbRewards[i] = 0;
				_ucbPulls[i] = 0;
			}

			EnsureRestoreBucketArrays();
			DecayRestorePosteriorsTowardUniform();

			if (width <= 0 || height <= 0 || sliceCount <= 0) {
				LastSessionTotalPixels = 0;
				LastSessionComplexity01 = 0f;
				_sessionSliceCapMul = 1f;
				_sessionBaseBudgetMul = 1f;
				_sessionMaxBudgetMul = 1f;
				_ewmaAlpha = 0.12f;
				_lastArm = 1;
				_restoreContextBucket = 0;
				return;
			}

			EvaluateWorkload(width, height, sliceCount, referencePixelsPerSlice, out long totalPixels, out float complexity01, out float totalLoad);
			LastSessionTotalPixels = totalPixels;
			LastSessionComplexity01 = complexity01;
			_restoreContextBucket = QuantizeContextBucket(complexity01, sliceCount, restoreContextBucketCount);

			_sessionSliceCapMul = 1f / Mathf.Max(0.2f, Mathf.Sqrt(Mathf.Max(1f, totalLoad / 4f)));
			_sessionSliceCapMul = Mathf.Clamp(_sessionSliceCapMul, 0.2f, 1f);

			_sessionBaseBudgetMul = Mathf.Lerp(1f, 1.45f, complexity01);
			_sessionMaxBudgetMul = Mathf.Lerp(1f, 1.65f, complexity01);

			_ewmaAlpha = Mathf.Lerp(0.12f, 0.22f, complexity01);

			_lastArm = complexity01 > 0.42f ? 0 : 1;
		}

		public void ObserveFrame(float deltaTime) {
			float hitch = Mathf.Max(0f, (deltaTime - (1f / 60f)) * 1000f);
			_ewmaHitchMs = Mathf.Lerp(_ewmaHitchMs, hitch, _ewmaAlpha);
		}

		float AgingMultiplier() {
			float waited = Time.realtimeSinceStartup - _restoreStartedRealtime;
			return Mathf.Min(agingMaxMultiplier, 1f + waited * agingBoostPerSecond);
		}

		bool UseAdaptiveRestore => restoreBudgetPolicy == RestoreBudgetPolicy.Ucb1 || restoreBudgetPolicy == RestoreBudgetPolicy.Thompson;

		/// <summary>Call once per frame before GetFrameBudget while restoring (picks bandit arm for this tick).</summary>
		public void BeginRestoreTick(float deltaTime) {
			ObserveFrame(deltaTime);
			if (restoreBudgetPolicy == RestoreBudgetPolicy.FixedMiddle) {
				_lastArm = 1;
				return;
			}
			if (restoreBudgetPolicy == RestoreBudgetPolicy.Ucb1) {
				_totalPullsUcb++;
				int best = 0;
				double bestScore = double.NegativeInfinity;
				for (int a = 0; a < RestoreArmCount; a++) {
					double mean = _ucbPulls[a] > 0 ? _ucbRewards[a] / _ucbPulls[a] : 0;
					double bonus = Math.Sqrt(2 * Math.Log(Math.Max(1, _totalPullsUcb)) / Math.Max(1, _ucbPulls[a]));
					double score = mean + bonus;
					if (score > bestScore) {
						bestScore = score;
						best = a;
					}
				}
				_lastArm = best;
				return;
			}

			// Thompson sampling (contextual bucket)
			EnsureRestoreBucketArrays();
			int b = Mathf.Clamp(_restoreContextBucket, 0, _restoreThompsonAlpha.GetLength(0) - 1);
			int bestT = 0;
			double bestSample = double.NegativeInfinity;
			for (int a = 0; a < RestoreArmCount; a++) {
				double s = SampleBeta01(_restoreThompsonAlpha[b, a], _restoreThompsonBeta[b, a]);
				if (s > bestSample) {
					bestSample = s;
					bestT = a;
				}
			}
			_lastArm = bestT;
		}

		/// <summary>UCB path: continuous reward. Thompson path: Bernoulli from hitch/upload.</summary>
		public void RegisterRestoreBanditObservation(float hitchMs, int uploaded) {
			if (!UseAdaptiveRestore) return;
			int a = Mathf.Clamp(_lastArm, 0, RestoreArmCount - 1);
			if (restoreBudgetPolicy == RestoreBudgetPolicy.Ucb1) {
				float reward = -hitchMs * 0.01f + uploaded * 0.15f;
				_ucbPulls[a]++;
				_ucbRewards[a] += reward;
				return;
			}
			if (restoreBudgetPolicy == RestoreBudgetPolicy.Thompson) {
				EnsureRestoreBucketArrays();
				int b = Mathf.Clamp(_restoreContextBucket, 0, _restoreThompsonAlpha.GetLength(0) - 1);
				bool success = hitchMs < restoreThompsonSuccessHitchMs && uploaded > 0;
				if (success) _restoreThompsonAlpha[b, a] += 1;
				else _restoreThompsonBeta[b, a] += 1;
			}
		}

		[Obsolete("Use RegisterRestoreBanditObservation")]
		public void RegisterUcbReward(float reward) {
			if (!UseAdaptiveRestore || restoreBudgetPolicy != RestoreBudgetPolicy.Ucb1) return;
			int a = Mathf.Clamp(_lastArm, 0, RestoreArmCount - 1);
			_ucbPulls[a]++;
			_ucbRewards[a] += reward;
		}

		/// <summary>Uses arm selected in BeginRestoreTick.</summary>
		public void GetFrameBudget(int slicesRemaining, out float budgetMs, out int maxSlices) {
			float aging = AgingMultiplier();
			float mul = UseAdaptiveRestore ? ArmBudgetMul[_lastArm] : 1f;
			float maxBudgetCap = maxBudgetMs * _sessionMaxBudgetMul;
			budgetMs = Mathf.Clamp(baseBudgetMs * _sessionBaseBudgetMul * mul * aging - _ewmaHitchMs * 0.25f, minBudgetMs, maxBudgetCap);
			int capSlices = Mathf.Max(minSlicesPerFrame, Mathf.RoundToInt(maxSlicesPerFrame * _sessionSliceCapMul));
			maxSlices = Mathf.Clamp(Mathf.RoundToInt(capSlices * aging * mul), minSlicesPerFrame, capSlices);
			if (slicesRemaining < maxSlices) maxSlices = slicesRemaining;
		}

		/// <summary>Restore upload order: slice indices 0..n-1. Full pixel data per slice already exists in RAM; this is only scheduling order, not placeholder UDIMs. Visibility-based permutation can replace this later.</summary>
		public static int[] LinearSliceUploadOrder(int sliceCount) {
			var o = new int[sliceCount];
			for (int i = 0; i < sliceCount; i++) o[i] = i;
			return o;
		}

		static double SampleBeta01(double alpha, double beta) {
			double x = SampleGammaMT(Math.Max(1e-6, alpha));
			double y = SampleGammaMT(Math.Max(1e-6, beta));
			return x / (x + y);
		}

		static double SampleGammaMT(double shape) {
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

		static double SampleStdNormal() {
			double u1 = UnityEngine.Random.value + 1e-10;
			double u2 = UnityEngine.Random.value + 1e-10;
			return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(Math.PI * 2 * u2);
		}
	}
}
