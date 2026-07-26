using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>Scout → specialist → reconcile with RoutePlan sparsity / early-exit.</summary>
	public sealed class StageDagRuntime {
		public struct StageResult {
			public int StagesRun;
			public int NodesRun;
			public float Confidence;
			public float[] Fused;
			public string[] FiredNodes;
		}

		public StageResult Execute(RoutePlan plan, float[] bodyVector, float priorConfidence) {
			var fired = new List<string>();
			int maxNodes = plan != null ? Math.Max(1, plan.MaxNodes) : 4;
			float sparsity = plan != null ? Mathf.Clamp01(plan.ActivationSparsityBudget) : 0f;
			float early = plan != null ? Mathf.Clamp01(plan.EarlyExitConfidenceThreshold) : 1f;
			int stagesBudget = plan != null ? Math.Max(1, plan.MaxStages) : 3;
			int nodesRun = 0;
			int stagesRun = 0;
			float conf = Mathf.Clamp01(priorConfidence);
			float[] fused = bodyVector != null ? (float[])bodyVector.Clone() : new float[DecimaconDims.Width];

			if (plan?.Stages == null || plan.Stages.Count == 0) {
				return new StageResult {
					StagesRun = 0,
					NodesRun = 0,
					Confidence = conf,
					Fused = fused,
					FiredNodes = Array.Empty<string>(),
				};
			}

			foreach (var stage in plan.Stages) {
				if (stagesRun >= stagesBudget) break;
				if (conf >= early && stagesRun > 0) break;
				stagesRun++;
				var ids = stage.ParallelNodeIds ?? Array.Empty<string>();
				int allow = Math.Max(1, Mathf.CeilToInt(ids.Length * (1f - sparsity)));
				allow = Math.Min(allow, Math.Max(0, maxNodes - nodesRun));
				for (int i = 0; i < ids.Length && i < allow; i++) {
					fired.Add(ids[i]);
					nodesRun++;
					// Lightweight expert mix: bias fused vector by node hash.
					MixNode(fused, ids[i], 0.05f);
					conf = Mathf.Clamp01(conf + 0.04f * (1f - sparsity));
				}
				if (nodesRun >= maxNodes) break;
			}

			return new StageResult {
				StagesRun = stagesRun,
				NodesRun = nodesRun,
				Confidence = conf,
				Fused = fused,
				FiredNodes = fired.ToArray(),
			};
		}

		static void MixNode(float[] fused, string nodeId, float scale) {
			unchecked {
				uint h = 2166136261u;
				for (int i = 0; i < nodeId.Length; i++) {
					h ^= nodeId[i];
					h *= 16777619u;
				}
				for (int d = 0; d < fused.Length; d++) {
					h ^= (uint)d;
					h *= 16777619u;
					float u = (h % 10000u) / 10000f;
					fused[d] += (u * 2f - 1f) * scale;
				}
			}
		}
	}
}
