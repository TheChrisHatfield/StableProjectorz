using System;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>
	/// C# port of soil LavadSmartScheduler — Thompson 4-arm + Σ encode (Q=16).
	/// Not PaintUndo. Not the thin Pass B hitch-only gate.
	/// </summary>
	public sealed class LavadSmartScheduler {
		readonly System.Random _rng;
		readonly BanditArmState[] _arms = new BanditArmState[4];
		float _rareness = 0.15f;
		float _explorationBoost;
		bool _regimeShift;
		float _hitchEwmaMs;
		SchedulerSignalPacket _latest;
		bool _hasLatest;

		public LavadSmartScheduler(int? seed = null) {
			_rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
			for (int i = 0; i < 4; i++) {
				_arms[i] = new BanditArmState {
					Arm = (BanditArm)i,
					Alpha = i == (int)BanditArm.LatencyCritical ? 2f : 1f,
					Beta = 1f,
					Pulls = 0,
				};
			}
		}

		public float HitchEwmaMs => _hitchEwmaMs;
		public bool HasLatest => _hasLatest;
		public SchedulerSignalPacket Latest => _latest;
		public BanditArm? ForceArmForTests { get; set; }

		public float GetAlpha(BanditArm arm) => _arms[(int)arm].Alpha;
		public float GetBeta(BanditArm arm) => _arms[(int)arm].Beta;
		public int GetPulls(BanditArm arm) => _arms[(int)arm].Pulls;

		public void ObserveHitchMs(float ms) {
			if (!float.IsFinite(ms) || ms < 0f) return;
			_hitchEwmaMs = _hitchEwmaMs <= 0f ? ms : (_hitchEwmaMs * 0.85f + ms * 0.15f);
		}

		public SchedulerSignalPacket Dispatch(TelemetrySnapshot telemetry) {
			int cores = Math.Max(1, telemetry.CoreCount);
			if (Math.Abs(cores - (_hasLatest ? Math.Max(1, (int)_latest.WorkerPoolSize) : cores)) >= 2) {
				_regimeShift = true;
				_explorationBoost = Math.Max(_explorationBoost, 0.25f);
			}

			var arm = ForceArmForTests ?? ThompsonSelect();
			float latencyBudget = telemetry.LatencyBudgetMs;
			if (telemetry.ECoreCount > 0 && telemetry.QueuedTasks >= 4)
				latencyBudget = Math.Min(latencyBudget, 10f);

			float computeBudget = Math.Max(1f, cores * 2f - telemetry.QueuedTasks * 0.5f);
			int workerPool = cores;
			if (telemetry.QueuedTasks >= 8)
				workerPool = Math.Max(1, cores / 2);

			var packet = new SchedulerSignalPacket {
				SelectedArm = arm,
				PolicySample = SampleBeta(_arms[(int)arm].Alpha, _arms[(int)arm].Beta),
				LatencyBudget = latencyBudget,
				PowerConstraint = telemetry.PowerConstraint,
				ComputeBudgetUnits = computeBudget,
				WorkerPoolSize = workerPool,
				SignalsStable = true,
				EncodedSchedulerState = new float[DecimaconDims.Q],
			};

			var intent = ExtraLavdArmMap.IntentForArm(arm);
			packet.RoutingHints["scheduler_intent"] = (float)intent;
			packet.RoutingHints["sparsity_hint"] = arm == BanditArm.EnergyBalance ? 0.4f : 0.1f;
			packet.RoutingHints["e_core_active"] = telemetry.ECoreCount > 0 && latencyBudget <= 10f ? 1f : 0f;
			packet.ResourceSignals["queued_tasks"] = telemetry.QueuedTasks;
			packet.ResourceSignals["hitch_ewma"] = telemetry.HitchEwmaMs;
			packet.ResourceSignals["tip_velocity"] = telemetry.TipVelocity01;

			EncodeSigma(packet, telemetry);
			_arms[(int)arm].Pulls++;
			_latest = packet;
			_hasLatest = true;
			return packet;
		}

		public bool UpdateBandit(PerformanceFeedback feedback) {
			LavdPaintBoundary.RefuseBanditToPaintDto();
			if (!_hasLatest || _latest.LatencyBudget <= 0f) return false;
			float budget = _latest.LatencyBudget;
			float eq = feedback.EvidenceQuality;
			if (eq <= 0f) {
				float latTerm = 1f - feedback.ActualLatencyMs / budget;
				eq = Mathf.Clamp(0.5f * feedback.ActualAccuracy + 0.5f * latTerm, 0.2f, 1f);
			}
			bool success = feedback.ActualAccuracy > 0.95f && feedback.ActualLatencyMs < budget;
			int ix = (int)feedback.SelectedArm;
			if (ix < 0 || ix > 3) ix = (int)_latest.SelectedArm;
			if (success) _arms[ix].Alpha += eq;
			else _arms[ix].Beta += eq;
			if (_regimeShift && _explorationBoost > 0f) {
				_explorationBoost *= 0.9f;
				if (_explorationBoost < 0.05f) {
					_regimeShift = false;
					_explorationBoost = 0f;
				}
			}
			return true;
		}

		BanditArm ThompsonSelect() {
			int totalPulls = 1;
			for (int i = 0; i < 4; i++) totalPulls += _arms[i].Pulls;
			BanditArm best = BanditArm.Throughput;
			float bestSample = -1f;
			for (int i = 0; i < 4; i++) {
				float sample = SampleBeta(Mathf.Max(_arms[i].Alpha, 1e-6f), Mathf.Max(_arms[i].Beta, 1e-6f));
				float overuse = _arms[i].Pulls / (float)totalPulls;
				sample *= 1f - _rareness * overuse;
				if (_explorationBoost > 0f && _arms[i].Pulls == 0)
					sample += _explorationBoost;
				if (sample > bestSample) {
					bestSample = sample;
					best = _arms[i].Arm;
				}
			}
			return best;
		}

		float SampleBeta(float a, float b) {
			// Gamma ratio approximation via Unity Random is weak; use Knuth-style via System.Random.
			double x = SampleGamma(a);
			double y = SampleGamma(b);
			if (x + y <= 0) return 0.5f;
			return (float)(x / (x + y));
		}

		double SampleGamma(float shape) {
			// Marsaglia-Tsang for shape >= 1; for shape < 1 boost.
			if (shape < 1f) {
				double u = _rng.NextDouble();
				return SampleGamma(shape + 1f) * Math.Pow(u, 1.0 / Math.Max(shape, 1e-6));
			}
			double d = shape - 1.0 / 3.0;
			double c = 1.0 / Math.Sqrt(9.0 * d);
			while (true) {
				double x, v;
				do {
					x = Normal01();
					v = 1.0 + c * x;
				} while (v <= 0);
				v = v * v * v;
				double u = _rng.NextDouble();
				if (u < 1.0 - 0.0331 * (x * x) * (x * x)) return d * v;
				if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v))) return d * v;
			}
		}

		double Normal01() {
			double u1 = Math.Max(1e-12, _rng.NextDouble());
			double u2 = _rng.NextDouble();
			return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
		}

		static void EncodeSigma(SchedulerSignalPacket packet, TelemetrySnapshot telemetry) {
			// Stub Σ adapter: fixed-width features from scheduler packet (soil packet_to_features).
			float[] s = packet.EncodedSchedulerState;
			s[0] = (int)packet.SelectedArm / 4f;
			s[1] = packet.LatencyBudget / 100f;
			s[2] = packet.PowerConstraint;
			s[3] = packet.ComputeBudgetUnits / 32f;
			s[4] = packet.PolicySample;
			s[5] = packet.WorkerPoolSize / 64f;
			s[6] = telemetry.CoreCount / 64f;
			s[7] = telemetry.QueuedTasks / 32f;
			s[8] = telemetry.HitchEwmaMs / 50f;
			s[9] = telemetry.TipVelocity01;
			s[10] = telemetry.ClientIdle ? 1f : 0f;
			s[11] = packet.RoutingHints.TryGetValue("scheduler_intent", out float intent) ? intent / 3f : 0f;
			s[12] = packet.RoutingHints.TryGetValue("sparsity_hint", out float sp) ? sp : 0f;
			s[13] = packet.RoutingHints.TryGetValue("e_core_active", out float e) ? e : 0f;
			s[14] = Mathf.Clamp01(telemetry.PCoreCount / 32f);
			s[15] = Mathf.Clamp01(telemetry.ECoreCount / 32f);
			// Soft L2 normalize
			float n2 = 0f;
			for (int i = 0; i < DecimaconDims.Q; i++) n2 += s[i] * s[i];
			float inv = n2 > 1e-8f ? 1f / Mathf.Sqrt(n2) : 1f;
			for (int i = 0; i < DecimaconDims.Q; i++) s[i] *= inv;
		}
	}
}
