using System;
using System.Collections.Generic;
using UnityEngine;

namespace spz.MlpDecimacon {

	public static class DecimaconDims {
		public const int Q = 16;
		public const int Struct = 16;
		public const int Width = 96;
		public const int Layers = 5;
		public const int Heads = 4;
		public const int Window = 12;
	}

	/// <summary>Soil BanditArm (4) — Thompson scheduling policies.</summary>
	public enum BanditArm {
		LatencyCritical = 0,
		Throughput = 1,
		EnergyBalance = 2,
		Heterogeneous = 3,
	}

	public enum SchedulerIntent {
		FavorLatency = 0,
		FavorThroughput = 1,
		FavorEfficiency = 2,
		FavorHeterogeneous = 3,
	}

	/// <summary>EXTRALAVD narrative (3) — product Live/Propose story arms.</summary>
	public enum ExtraLavdNarrativeArm {
		UltraLean = 0,
		Balanced = 1,
		Burst = 2,
	}

	public struct RouteStage {
		public int StageIndex;
		public string[] ParallelNodeIds;
		public float ComputeBudget;
	}

	public sealed class RoutePlan {
		public List<RouteStage> Stages = new List<RouteStage>();
		public int MaxNodes;
		public int MaxStages;
		public float ActivationSparsityBudget;
		public float EarlyExitConfidenceThreshold;
		public float RouteConfidence;
		public Dictionary<string, float> NodeSoftmaxWeights;
	}

	public sealed class SchedulerSignalPacket {
		public BanditArm SelectedArm;
		public float PolicySample;
		public float LatencyBudget = 50f;
		public float PowerConstraint = 0.5f;
		public float ComputeBudgetUnits = 8f;
		public int WorkerPoolSize = 4;
		public bool SignalsStable = true;
		public float[] EncodedSchedulerState = new float[DecimaconDims.Q];
		public Dictionary<string, float> RoutingHints = new Dictionary<string, float>();
		public Dictionary<string, float> ResourceSignals = new Dictionary<string, float>();
	}

	public struct TelemetrySnapshot {
		public int CoreCount;
		public int PCoreCount;
		public int ECoreCount;
		public float[] CpuUtilizations;
		public int QueuedTasks;
		public float LatencyBudgetMs;
		public float PowerConstraint;
		public bool ClientIdle;
		public float HitchEwmaMs;
		public float TipVelocity01;

		public static TelemetrySnapshot ForLive(float hitchEwmaMs, float tipVelocity01 = 0f) {
			int cores = SystemInfo.processorCount > 0 ? SystemInfo.processorCount : 4;
			return new TelemetrySnapshot {
				CoreCount = cores,
				PCoreCount = Math.Max(1, cores / 2),
				ECoreCount = Math.Max(0, cores / 2),
				CpuUtilizations = new[] { 0.35f, 0.4f, 0.3f, 0.45f },
				QueuedTasks = hitchEwmaMs > 12f ? 6 : 2,
				LatencyBudgetMs = hitchEwmaMs > 16f ? 8f : 50f,
				PowerConstraint = 0.5f,
				ClientIdle = false,
				HitchEwmaMs = hitchEwmaMs,
				TipVelocity01 = tipVelocity01,
			};
		}

		public static TelemetrySnapshot ForPropose(float hitchEwmaMs) {
			var t = ForLive(hitchEwmaMs);
			t.ClientIdle = true;
			t.QueuedTasks = Math.Max(0, t.QueuedTasks - 1);
			t.LatencyBudgetMs = Math.Max(t.LatencyBudgetMs, 25f);
			return t;
		}
	}

	public struct PerformanceFeedback {
		public BanditArm SelectedArm;
		public float ActualLatencyMs;
		public float ActualAccuracy;
		public float EvidenceQuality;
	}

	public struct BanditArmState {
		public BanditArm Arm;
		public float Alpha;
		public float Beta;
		public int Pulls;
	}

	public static class ExtraLavdArmMap {
		public static BanditArm BanditForNarrative(ExtraLavdNarrativeArm arm) {
			switch (arm) {
				case ExtraLavdNarrativeArm.UltraLean: return BanditArm.EnergyBalance;
				case ExtraLavdNarrativeArm.Burst: return BanditArm.LatencyCritical;
				default: return BanditArm.Throughput;
			}
		}

		public static ExtraLavdNarrativeArm NarrativeForBandit(BanditArm arm) {
			switch (arm) {
				case BanditArm.EnergyBalance: return ExtraLavdNarrativeArm.UltraLean;
				case BanditArm.LatencyCritical: return ExtraLavdNarrativeArm.Burst;
				case BanditArm.Heterogeneous:
				case BanditArm.Throughput:
				default: return ExtraLavdNarrativeArm.Balanced;
			}
		}

		public static SchedulerIntent IntentForArm(BanditArm arm) {
			switch (arm) {
				case BanditArm.LatencyCritical: return SchedulerIntent.FavorLatency;
				case BanditArm.EnergyBalance: return SchedulerIntent.FavorEfficiency;
				case BanditArm.Heterogeneous: return SchedulerIntent.FavorHeterogeneous;
				default: return SchedulerIntent.FavorThroughput;
			}
		}

		public static int SaDepthForArm(BanditArm arm) {
			switch (arm) {
				case BanditArm.EnergyBalance: return 1;
				case BanditArm.LatencyCritical: return 5;
				default: return 3;
			}
		}
	}
}
