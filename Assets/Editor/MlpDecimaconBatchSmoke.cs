using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace spz.Editor {

	/// <summary>
	/// Batchmode entry: Unity -executeMethod spz.Editor.MlpDecimaconBatchSmoke.Run
	/// </summary>
	public static class MlpDecimaconBatchSmoke {

		public static void Run() {
			string outPath = Path.Combine(Application.dataPath, "..", "TestResults_MlpDecimacon_BatchSmoke.txt");
			try {
				var sb = new System.Text.StringBuilder();
				sb.AppendLine("MlpDecimaconBatchSmoke " + DateTime.UtcNow.ToString("o"));

				AssertTrue(spz.MlpDecimacon.RoutingHeadRuntime.TryCreate(out var head, out string err), "routing:" + err);
				var sched = new spz.MlpDecimacon.LavadSmartScheduler(seed: 3);
				var signal = sched.Dispatch(spz.MlpDecimacon.TelemetrySnapshot.ForPropose(0f));
				var plan = head.Plan(signal.EncodedSchedulerState, signal, 0.3f, 0.5f);
				AssertTrue(plan.MaxNodes >= 1, "max_nodes");
				AssertTrue(plan.MaxStages >= 1 && plan.MaxStages <= 3, "max_stages");
				sb.AppendLine("OK routing max_nodes=" + plan.MaxNodes + " max_stages=" + plan.MaxStages);

				AssertTrue(spz.MlpDecimacon.MlpDecimaconRuntime.TryCreate(out var rt, out err), "runtime:" + err);
				float[] feat = { 0.5f, 0.2f, 0.2f, 0.2f, 0.2f, 0.2f, 0.1f };
				var fr = rt.Forward(spz.MlpDecimacon.TelemetrySnapshot.ForPropose(0f), feat);
				AssertTrue(fr.BodyVector != null && fr.BodyVector.Length == 96, "body");
				AssertTrue(fr.ActiveLayers >= 1 && fr.ActiveLayers <= 5, "depth");
				sb.AppendLine("OK forward L=" + fr.ActiveLayers + " stages=" + fr.Stage.StagesRun + " hasValue=" + fr.HasValue);

				var assist = ValuePaintAssistFactory.Create(preferNeural: true, out string which);
				sb.AppendLine("factory=" + which);
				AssertTrue(which.IndexOf("MlpDecimaconPaintAssist", StringComparison.Ordinal) >= 0
				            || which.IndexOf("Deterministic", StringComparison.Ordinal) >= 0, "factory_which");
				if (MlpDecimaconPaintAssist.TryCreate(out var dec, out err)) {
					AssertTrue(assist is MlpDecimaconPaintAssist, "assist_type");
					var p = dec.ProposeFromLuminance(0.5f);
					AssertTrue(p.Source != null && p.Source.StartsWith("mlp_decimacon"), "source:" + p.Source);
					sb.AppendLine("OK propose " + p);
				} else {
					sb.AppendLine("WARN value heads unavailable: " + err);
				}

				sb.AppendLine("SMOKE PASS");
				File.WriteAllText(outPath, sb.ToString());
				Debug.Log("[MlpDecimaconBatchSmoke] PASS → " + outPath);
				EditorApplication.Exit(0);
			} catch (Exception e) {
				File.WriteAllText(outPath, "SMOKE FAIL\n" + e);
				Debug.LogError("[MlpDecimaconBatchSmoke] FAIL " + e);
				EditorApplication.Exit(1);
			}
		}

		static void AssertTrue(bool cond, string msg) {
			if (!cond) throw new Exception("ASSERT " + msg);
		}
	}
}
