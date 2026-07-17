using UnityEngine;

namespace spz {

	/// <summary>
	/// T5 — MLP <see cref="IValuePaintAssist"/> using exported T9.2 weights (CPU).
	/// Proposals only; stroke apply remains <see cref="ValuePaintProposalApplier.TryAccept"/>.
	/// </summary>
	public sealed class MlpValuePaintAssist : IValuePaintAssist {
		public const string ResourcesPath = "SmartValuePaint/multihead_weights";

		readonly ValuePaintMlpRuntime _net;
		readonly float[] _feat = new float[ValuePaintFeatureBuilder.FeatureDim];

		public string SourceTag { get; } = "mlp_t9";

		public MlpValuePaintAssist(ValuePaintMlpRuntime runtime) {
			_net = runtime ?? throw new System.ArgumentNullException(nameof(runtime));
		}

		public static bool TryCreate(out MlpValuePaintAssist assist, out string error) {
			assist = null;
			if (!ValuePaintMlpWeightsDto.TryLoadFromResources(ResourcesPath, out var dto, out error))
				return false;
			try {
				assist = new MlpValuePaintAssist(new ValuePaintMlpRuntime(dto));
				return true;
			} catch (System.Exception e) {
				error = e.Message;
				return false;
			}
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
			var o = _net.Forward(feat);
			float blend = o.Blend01;
			float edge = o.EdgeSoft01;
			float width = o.Width01;
			float opacity = o.Opacity01;
			if (strokeState.HasBrushHints) {
				// Mirror DeterministicValuePaintAssist: non-finite overrides must not poison Clamp01(NaN).
				if (float.IsFinite(strokeState.BlendStrength01)) blend = strokeState.BlendStrength01;
				if (float.IsFinite(strokeState.BrushWidth01)) width = strokeState.BrushWidth01;
				if (float.IsFinite(strokeState.Opacity01)) opacity = strokeState.Opacity01;
			}
			return new ValuePaintProposal {
				CurrentBin = (ValuePaintBand)Mathf.Clamp(o.CurrentBin, 0, 4),
				DesiredBin = (ValuePaintBand)Mathf.Clamp(o.DesiredBin, 0, 4),
				BlendStrength01 = Mathf.Clamp01(blend),
				EdgeSoftness01 = Mathf.Clamp01(edge),
				BrushWidthHint01 = Mathf.Clamp01(width),
				OpacityHint01 = Mathf.Clamp01(opacity),
				StrokeRole = (ValuePaintStrokeRole)Mathf.Clamp(o.StrokeRole, 0, 4),
				MeanLuminance01 = Mathf.Clamp01(float.IsFinite(feat[0]) ? feat[0] : 0.5f),
				Source = SourceTag,
			};
		}
	}

	/// <summary>Prefer MLP weights; fall back to deterministic stub.</summary>
	public static class ValuePaintAssistFactory {
		public static IValuePaintAssist Create(out string which) {
			if (MlpValuePaintAssist.TryCreate(out var mlp, out string mlpErr)) {
				which = "MlpValuePaintAssist";
				return mlp;
			}
			// Surface load failure in which — silent "_" hid why UI fell back to stub (false healthy).
			which = string.IsNullOrEmpty(mlpErr)
				? "DeterministicValuePaintAssist"
				: "DeterministicValuePaintAssist (mlp unavailable: " + mlpErr + ")";
			return new DeterministicValuePaintAssist();
		}

		public static IValuePaintAssist Create() {
			return Create(out _);
		}
	}

}
