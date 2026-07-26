using UnityEngine;
using spz.MlpDecimacon;

namespace spz {

	/// <summary>
	/// Pass D — soil MLP Decimacon + value heads → ValuePaintProposal.
	/// </summary>
	public sealed class MlpDecimaconPaintAssist : IValuePaintAssist {
		readonly MlpDecimaconRuntime _net;
		readonly float[] _feat = new float[ValuePaintFeatureBuilder.FeatureDim];

		public string SourceTag { get; } = "mlp_decimacon";

		public MlpDecimaconPaintAssist(MlpDecimaconRuntime runtime) {
			_net = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
		}

		public static bool TryCreate(out MlpDecimaconPaintAssist assist, out string error) {
			assist = null;
			if (!MlpDecimaconRuntime.TryCreate(out var runtime, out error, requireValueHeads: true))
				return false;
			assist = new MlpDecimaconPaintAssist(runtime);
			return true;
		}

		public ValuePaintProposal ProposeFromLuminance(float luminance01, ValuePaintStrokeState strokeState = default) {
			ValuePaintFeatureBuilder.FromLuminance(luminance01, _feat);
			return ProposeFromFeatures(_feat, strokeState);
		}

		public ValuePaintProposal ProposeFromColor(Color sample, ValuePaintStrokeState strokeState = default) {
			return ProposeFromLuminance(DeterministicValuePaintAssist.Luminance01(sample), strokeState);
		}

		public ValuePaintProposal ProposeFromColors(Color[] patch, ValuePaintStrokeState strokeState = default) {
			StrokeFeatureExtractor.ExtractFromColors(patch, _feat);
			return ProposeFromFeatures(_feat, strokeState);
		}

		ValuePaintProposal ProposeFromFeatures(float[] feat, ValuePaintStrokeState strokeState) {
			SchedulerSignalPacket signal = null;
			TelemetrySnapshot tel;
			if (DecimaconProductGate.HasLastDispatch) {
				signal = DecimaconProductGate.LastSignal;
				tel = TelemetrySnapshot.ForPropose(DecimaconProductGate.Scheduler.HitchEwmaMs);
				tel.LatencyBudgetMs = signal.LatencyBudget;
			} else {
				tel = TelemetrySnapshot.ForPropose(0f);
			}
			var fr = _net.Forward(tel, feat, taskValueScore: 0.55f, uncertainty: 0.3f, existingSignal: signal);
			if (!fr.HasValue)
				return new DeterministicValuePaintAssist().ProposeFromLuminance(feat[0], strokeState);

			var o = fr.Value;
			float blend = o.Blend01;
			float edge = o.EdgeSoft01;
			float width = o.Width01;
			float opacity = o.Opacity01;
			if (strokeState.HasBrushHints) {
				if (float.IsFinite(strokeState.BlendStrength01)) blend = strokeState.BlendStrength01;
				if (float.IsFinite(strokeState.BrushWidth01)) width = strokeState.BrushWidth01;
				if (float.IsFinite(strokeState.Opacity01)) opacity = strokeState.Opacity01;
			}

			var current = (ValuePaintBand)Mathf.Clamp(o.CurrentBin, 0, 4);
			var desired = (ValuePaintBand)Mathf.Clamp(o.DesiredBin, 0, 4);
			if (desired == current)
				desired = DeterministicValuePaintAssist.DesireAdjacentValueStep(current);

			var role = (ValuePaintStrokeRole)Mathf.Clamp(o.StrokeRole, 0, 4);
			if (current != desired)
				role = DeterministicValuePaintAssist.RoleForTransition(current, desired);
			if (current != desired && (!float.IsFinite(blend) || blend < 0.55f))
				blend = 0.75f;

			return new ValuePaintProposal {
				CurrentBin = current,
				DesiredBin = desired,
				BlendStrength01 = Mathf.Clamp01(blend),
				EdgeSoftness01 = Mathf.Clamp01(edge),
				BrushWidthHint01 = Mathf.Clamp01(width),
				OpacityHint01 = Mathf.Clamp01(opacity),
				StrokeRole = role,
				MeanLuminance01 = Mathf.Clamp01(float.IsFinite(feat[0]) ? feat[0] : 0.5f),
				Source = SourceTag + "/L" + fr.ActiveLayers + "/n" + fr.Stage.NodesRun,
			};
		}
	}
}
