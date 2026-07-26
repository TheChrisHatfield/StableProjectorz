using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>ONNX-I/O mirror of soil RoutingHeadMLP (float32 CPU).</summary>
	public sealed class RoutingHeadMlpWeightsDto {
		public string version;
		public string arch;
		public int q_dim = DecimaconDims.Q;
		public int struct_dim = DecimaconDims.Struct;
		public int hidden = 64;
		public int in_dim = 50;
		public int out_dim = 5;
		public float[] net_0_weight;
		public float[] net_0_bias;
		public float[] net_2_weight;
		public float[] net_2_bias;
		public float[] net_4_weight;
		public float[] net_4_bias;

		public const string StreamingRelative = "MlpDecimacon/routing_head_mlp_weights.json";

		public static bool TryLoad(out RoutingHeadMlpWeightsDto dto, out string error) {
			dto = null;
			error = null;
			try {
				string path = Path.Combine(Application.streamingAssetsPath, StreamingRelative.Replace('/', Path.DirectorySeparatorChar));
				if (!File.Exists(path)) {
					error = "missing " + path;
					return false;
				}
				dto = JsonConvert.DeserializeObject<RoutingHeadMlpWeightsDto>(File.ReadAllText(path));
				if (dto == null || dto.net_0_weight == null || dto.net_4_weight == null) {
					error = "invalid routing MLP weights";
					return false;
				}
				return true;
			} catch (Exception e) {
				error = e.Message;
				return false;
			}
		}
	}

	public sealed class RoutingHeadRuntime {
		readonly RoutingHeadMlpWeightsDto _w;
		readonly float[] _in = new float[50];
		readonly float[] _h1 = new float[64];
		readonly float[] _h2 = new float[64];
		readonly float[] _out = new float[5];

		static readonly string[] ScoutNodes = { "task_classifier", "difficulty_estimator" };
		static readonly string[] SpecialistNodes = { "code_expert", "critic_expert", "memory_expert" };
		static readonly string[] ReconcileNodes = { "forge_merge" };

		public RoutingHeadRuntime(RoutingHeadMlpWeightsDto w) {
			_w = w ?? throw new ArgumentNullException(nameof(w));
		}

		public static bool TryCreate(out RoutingHeadRuntime runtime, out string error) {
			runtime = null;
			if (!RoutingHeadMlpWeightsDto.TryLoad(out var dto, out error)) return false;
			runtime = new RoutingHeadRuntime(dto);
			return true;
		}

		public RoutePlan Plan(
			float[] sharedLatent16,
			SchedulerSignalPacket signal,
			float uncertaintyScore,
			float taskValueScore) {
			float[] sched = signal?.EncodedSchedulerState;
			PadCopy(sharedLatent16, _in, 0, DecimaconDims.Q);
			PadCopy(sched, _in, DecimaconDims.Q, DecimaconDims.Q);

			float budget = signal != null ? signal.LatencyBudget : 50f;
			int arm = signal != null ? (int)signal.SelectedArm : 0;
			float intent = 0f;
			if (signal != null && signal.RoutingHints != null && signal.RoutingHints.TryGetValue("scheduler_intent", out float iv))
				intent = iv / 3f;

			float[] structured = new float[DecimaconDims.Struct];
			structured[0] = budget / 100f;
			structured[1] = arm / 4f;
			structured[2] = uncertaintyScore;
			structured[3] = taskValueScore;
			structured[4] = intent;
			PadCopy(structured, _in, DecimaconDims.Q * 2, DecimaconDims.Struct);
			_in[48] = uncertaintyScore;
			_in[49] = taskValueScore;

			Linear(_w.net_0_weight, _w.net_0_bias, _in, 50, _h1, 64);
			Gelu(_h1);
			Linear(_w.net_2_weight, _w.net_2_bias, _h1, 64, _h2, 64);
			Gelu(_h2);
			Linear(_w.net_4_weight, _w.net_4_bias, _h2, 64, _out, 5);

			int maxStages = Mathf.Clamp(Mathf.RoundToInt(_out[1]), 1, 3);
			var plan = new RoutePlan {
				MaxNodes = Math.Max(1, Mathf.RoundToInt(_out[0])),
				MaxStages = maxStages,
				ActivationSparsityBudget = Mathf.Clamp01(_out[2]),
				EarlyExitConfidenceThreshold = Mathf.Clamp01(_out[3]),
				RouteConfidence = Mathf.Clamp01(_out[4]),
			};
			plan.Stages.Add(new RouteStage { StageIndex = 0, ParallelNodeIds = ScoutNodes, ComputeBudget = 1f });
			if (maxStages >= 2)
				plan.Stages.Add(new RouteStage { StageIndex = 1, ParallelNodeIds = SpecialistNodes, ComputeBudget = 1f });
			if (maxStages >= 3)
				plan.Stages.Add(new RouteStage { StageIndex = 2, ParallelNodeIds = ReconcileNodes, ComputeBudget = 1f });
			return plan;
		}

		static void PadCopy(float[] src, float[] dst, int dstOff, int count) {
			for (int i = 0; i < count; i++)
				dst[dstOff + i] = src != null && i < src.Length ? src[i] : 0f;
		}

		static void Linear(float[] w, float[] b, float[] x, int inDim, float[] y, int outDim) {
			for (int o = 0; o < outDim; o++) {
				float s = b != null && o < b.Length ? b[o] : 0f;
				int row = o * inDim;
				for (int i = 0; i < inDim; i++)
					s += w[row + i] * x[i];
				y[o] = s;
			}
		}

		static void Gelu(float[] v) {
			for (int i = 0; i < v.Length; i++) {
				float x = v[i];
				v[i] = 0.5f * x * (1f + (float)System.Math.Tanh(0.7978845608 * (x + 0.044715 * x * x * x)));
			}
		}
	}
}
