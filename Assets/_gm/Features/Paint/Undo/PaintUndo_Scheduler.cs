using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>Per-frame restore budget: EWMA hitch proxy, LAVD-style aging, optional UCB1 over 3 discrete budget arms.
	/// <see cref="BeginRestoreSession"/> scales caps from resolution × slice count (4K / many UDIMs → fewer slices per frame, wider time budget).</summary>
	public class PaintUndo_Scheduler {

		public float baseBudgetMs = 2.5f;
		public float minBudgetMs = 0.75f;
		public float maxBudgetMs = 8f;
		public int minSlicesPerFrame = 1;
		public int maxSlicesPerFrame = 8;
		public float agingBoostPerSecond = 0.35f;
		public float agingMaxMultiplier = 4f;
		public bool useUcbBudgetSelection = true;

		/// <summary>Reference pixel count (~one 512² UDIM). Loads above this tighten slice batching.</summary>
		public float referencePixelsPerSlice = 512f * 512f;

		float _ewmaHitchMs = 0f;
		float _ewmaAlpha = 0.12f;
		float _restoreStartedRealtime;
		int _totalPullsUcb;
		readonly double[] _ucbRewards = new double[3];
		readonly int[] _ucbPulls = new int[3];
		static readonly float[] ArmBudgetMul = { 0.6f, 1f, 1.5f };
		int _lastArm = 1;

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

		/// <summary>Start a new restore: reset bandit state, EWMA, and derive batch/budget multipliers from workload (no user tuning).</summary>
		public void BeginRestoreSession(int width, int height, int sliceCount) {
			_restoreStartedRealtime = Time.realtimeSinceStartup;
			_ewmaHitchMs = 0f;
			_totalPullsUcb = 0;
			for (int i = 0; i < 3; i++) {
				_ucbRewards[i] = 0;
				_ucbPulls[i] = 0;
			}

			if (width <= 0 || height <= 0 || sliceCount <= 0) {
				LastSessionTotalPixels = 0;
				LastSessionComplexity01 = 0f;
				_sessionSliceCapMul = 1f;
				_sessionBaseBudgetMul = 1f;
				_sessionMaxBudgetMul = 1f;
				_ewmaAlpha = 0.12f;
				_lastArm = 1;
				return;
			}

			EvaluateWorkload(width, height, sliceCount, referencePixelsPerSlice, out long totalPixels, out float complexity01, out float totalLoad);
			LastSessionTotalPixels = totalPixels;
			LastSessionComplexity01 = complexity01;

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

		/// <summary>Call once per frame before GetFrameBudget while restoring (picks UCB arm for this tick).</summary>
		public void BeginRestoreTick(float deltaTime) {
			ObserveFrame(deltaTime);
			if (!useUcbBudgetSelection) {
				_lastArm = 1;
				return;
			}
			_totalPullsUcb++;
			int best = 0;
			double bestScore = double.NegativeInfinity;
			for (int a = 0; a < 3; a++) {
				double mean = _ucbPulls[a] > 0 ? _ucbRewards[a] / _ucbPulls[a] : 0;
				double bonus = Math.Sqrt(2 * Math.Log(Math.Max(1, _totalPullsUcb)) / Math.Max(1, _ucbPulls[a]));
				double score = mean + bonus;
				if (score > bestScore) {
					bestScore = score;
					best = a;
				}
			}
			_lastArm = best;
		}

		public void RegisterUcbReward(float reward) {
			if (!useUcbBudgetSelection) return;
			int a = Mathf.Clamp(_lastArm, 0, 2);
			_ucbPulls[a]++;
			_ucbRewards[a] += reward;
		}

		/// <summary>Uses arm selected in BeginRestoreTick.</summary>
		public void GetFrameBudget(int slicesRemaining, out float budgetMs, out int maxSlices) {
			float aging = AgingMultiplier();
			float mul = useUcbBudgetSelection ? ArmBudgetMul[_lastArm] : 1f;
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
	}
}
