using System;
using UnityEngine;

namespace spz.MlpDecimacon {

	/// <summary>
	/// Soil MLP Decimacon forward: LAVD packet → RoutePlan → TransformerLite → stage DAG → optional value heads.
	/// </summary>
	public sealed class MlpDecimaconRuntime {
		readonly LavadSmartScheduler _scheduler;
		readonly RoutingHeadRuntime _routing;
		readonly TransformerLiteBody _body;
		readonly StageDagRuntime _dag;
		readonly ValueHeadsRuntime _valueHeads; // may be null until Phase 2 weights
		readonly float[] _shared = new float[DecimaconDims.Q];
		readonly float[] _featProj = new float[DecimaconDims.Width];

		public LavadSmartScheduler Scheduler => _scheduler;
		public TransformerLiteBody Body => _body;
		public bool HasValueHeads => _valueHeads != null;

		public struct ForwardResult {
			public SchedulerSignalPacket Signal;
			public RoutePlan Plan;
			public int ActiveLayers;
			public float[] BodyVector;
			public StageDagRuntime.StageResult Stage;
			public ValueHeadsRuntime.Output Value;
			public bool HasValue;
		}

		public MlpDecimaconRuntime(
			LavadSmartScheduler scheduler,
			RoutingHeadRuntime routing,
			TransformerLiteBody body,
			StageDagRuntime dag,
			ValueHeadsRuntime valueHeads = null) {
			_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
			_routing = routing ?? throw new ArgumentNullException(nameof(routing));
			_body = body ?? throw new ArgumentNullException(nameof(body));
			_dag = dag ?? throw new ArgumentNullException(nameof(dag));
			_valueHeads = valueHeads;
		}

		public static bool TryCreate(out MlpDecimaconRuntime runtime, out string error, bool requireValueHeads = false) {
			runtime = null;
			if (!RoutingHeadRuntime.TryCreate(out var routing, out error)) return false;
			ValueHeadsRuntime value = null;
			if (ValueHeadsRuntime.TryCreate(out var vh, out string vhErr))
				value = vh;
			else if (requireValueHeads) {
				error = vhErr ?? "value heads missing";
				return false;
			}
			runtime = new MlpDecimaconRuntime(
				new LavadSmartScheduler(seed: 7),
				routing,
				TransformerLiteBody.CreatePreferWarmStart(),
				new StageDagRuntime(),
				value);
			error = null;
			return true;
		}

		public ForwardResult Forward(
			TelemetrySnapshot telemetry,
			float[] features7OrNull,
			float taskValueScore = 0.5f,
			float uncertainty = 0.35f,
			SchedulerSignalPacket existingSignal = null) {
			var signal = existingSignal ?? _scheduler.Dispatch(telemetry);
			if (existingSignal == null) {
				// keep runtime scheduler in sync only when we own dispatch
			} else if (!_scheduler.HasLatest) {
				// no-op: product gate already dispatched on its own scheduler instance
			}
			BuildSharedLatent(features7OrNull, signal);
			var plan = _routing.Plan(_shared, signal, uncertainty, taskValueScore);
			int depth = ExtraLavdArmMap.SaDepthForArm(signal.SelectedArm);
			ProjectFeaturesToWidth(features7OrNull, signal);
			var bodyVec = _body.ForwardVector(_featProj, depth);
			var stage = _dag.Execute(plan, bodyVec, plan.RouteConfidence);
			var result = new ForwardResult {
				Signal = signal,
				Plan = plan,
				ActiveLayers = depth,
				BodyVector = bodyVec,
				Stage = stage,
				HasValue = false,
			};
			if (_valueHeads != null && features7OrNull != null) {
				result.Value = _valueHeads.Forward(stage.Fused, features7OrNull);
				result.HasValue = true;
			}
			return result;
		}

		void BuildSharedLatent(float[] features7, SchedulerSignalPacket signal) {
			for (int i = 0; i < DecimaconDims.Q; i++)
				_shared[i] = signal.EncodedSchedulerState[i];
			if (features7 == null) return;
			for (int i = 0; i < Math.Min(features7.Length, DecimaconDims.Q); i++)
				_shared[i] = 0.65f * _shared[i] + 0.35f * features7[i % features7.Length];
		}

		void ProjectFeaturesToWidth(float[] features7, SchedulerSignalPacket signal) {
			for (int d = 0; d < DecimaconDims.Width; d++) {
				float v = signal.EncodedSchedulerState[d % DecimaconDims.Q];
				if (features7 != null && features7.Length > 0)
					v = 0.5f * v + 0.5f * features7[d % features7.Length];
				_featProj[d] = v;
			}
		}
	}
}
