using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz {

	/// <summary>Per-frame restore budget: EWMA hitch proxy, LAVD-style aging, optional UCB1 over 3 discrete budget arms.</summary>
	public class PaintUndo_Scheduler {

		public float baseBudgetMs = 2.5f;
		public float minBudgetMs = 0.75f;
		public float maxBudgetMs = 8f;
		public int minSlicesPerFrame = 1;
		public int maxSlicesPerFrame = 8;
		public float agingBoostPerSecond = 0.35f;
		public float agingMaxMultiplier = 4f;
		public bool useUcbBudgetSelection = true;

		float _ewmaHitchMs = 0f;
		const float EwmaAlpha = 0.12f;
		float _restoreStartedRealtime;
		int _totalPullsUcb;
		readonly double[] _ucbRewards = new double[3];
		readonly int[] _ucbPulls = new int[3];
		static readonly float[] ArmBudgetMul = { 0.6f, 1f, 1.5f };
		int _lastArm = 1;

		public void ResetSession() {
			_restoreStartedRealtime = Time.realtimeSinceStartup;
		}

		public void ObserveFrame(float deltaTime) {
			float hitch = Mathf.Max(0f, (deltaTime - (1f / 60f)) * 1000f);
			_ewmaHitchMs = Mathf.Lerp(_ewmaHitchMs, hitch, EwmaAlpha);
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
			budgetMs = Mathf.Clamp(baseBudgetMs * mul * aging - _ewmaHitchMs * 0.25f, minBudgetMs, maxBudgetMs);
			maxSlices = Mathf.Clamp(Mathf.RoundToInt(maxSlicesPerFrame * aging * mul), minSlicesPerFrame, maxSlicesPerFrame);
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
