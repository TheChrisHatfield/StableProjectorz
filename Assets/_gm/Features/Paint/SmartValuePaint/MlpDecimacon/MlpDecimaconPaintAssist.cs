using UnityEngine;
using spz.MlpDecimacon;

namespace spz {

	/// <summary>
	/// Pass D — soil MLP Decimacon + value heads → ValuePaintProposal.
	/// </summary>
	public sealed class MlpDecimaconPaintAssist : IValuePaintAssist {
		readonly MlpDecimaconRuntime _net;
		readonly float[] _feat = new float[ValuePaintFeatureBuilder.FeatureDim];
		// Deterministic is a prior/input here, never a silent replacement (brush-behavior B8.1).
		readonly DeterministicValuePaintAssist _prior = new DeterministicValuePaintAssist();

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

			// B8.2 — deterministic prior conditions the forward instead of hardcoded constants.
			float priorLum = Mathf.Clamp01(float.IsFinite(feat[0]) ? feat[0] : 0.5f);
			ValuePaintProposal prior = _prior.ProposeFromLuminance(priorLum, strokeState);
			float priorEdge = feat.Length > 6 && float.IsFinite(feat[6]) ? Mathf.Clamp01(feat[6]) : 0.15f;
			float taskValueScore = TaskValueFromPrior(prior.CurrentBin, priorEdge);
			float uncertainty = UncertaintyFromPrior(priorLum, priorEdge);

			var fr = _net.Forward(tel, feat, taskValueScore, uncertainty, existingSignal: signal);
			if (!fr.HasValue) {
				// B8.3 — only a forward with no value output may fall back wholesale.
				prior.Source = "DeterministicValuePaintAssist (decimacon no value output)";
				return prior;
			}

			var o = fr.Value;
			float headConf = 0.5f * (o.CurrentConfidence01 + o.DesiredConfidence01);
			DecimaconProductGate.ReportForwardQuality(fr.Plan.RouteConfidence, headConf, armSucceeded: true);

			// B8.3 — per-field prior fill for non-finite head outputs only; finite neural values win.
			string filled = null;
			float blend = FiniteOr(o.Blend01, prior.BlendStrength01, "blend", ref filled);
			float edge = FiniteOr(o.EdgeSoft01, prior.EdgeSoftness01, "edge", ref filled);
			float width = FiniteOr(o.Width01, prior.BrushWidthHint01, "width", ref filled);
			float opacity = FiniteOr(o.Opacity01, prior.OpacityHint01, "opacity", ref filled);
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

			// B8.4 — Source states what actually ran, including any prior-filled fields.
			string source = SourceTag + "/L" + fr.ActiveLayers + "/n" + fr.Stage.NodesRun;
			if (filled != null)
				source += "+prior:" + filled;

			return new ValuePaintProposal {
				CurrentBin = current,
				DesiredBin = desired,
				BlendStrength01 = Mathf.Clamp01(blend),
				EdgeSoftness01 = Mathf.Clamp01(edge),
				BrushWidthHint01 = Mathf.Clamp01(width),
				OpacityHint01 = Mathf.Clamp01(opacity),
				StrokeRole = role,
				MeanLuminance01 = priorLum,
				Source = source,
			};
		}

		static float FiniteOr(float neural, float priorValue, string field, ref string filled) {
			if (float.IsFinite(neural)) return neural;
			filled = filled == null ? field : filled + "|" + field;
			return float.IsFinite(priorValue) ? priorValue : 0.5f;
		}

		/// <summary>
		/// Prior-derived task value for routing: plane extremity (highlight/accent carry more
		/// structural weight than midtone) lifted by local edge energy.
		/// </summary>
		public static float TaskValueFromPrior(ValuePaintBand priorBand, float edgeMag01) {
			float extremity;
			switch (priorBand) {
				case ValuePaintBand.Highlight: extremity = 0.85f; break;
				case ValuePaintBand.AccentDark: extremity = 0.9f; break;
				case ValuePaintBand.Light: extremity = 0.6f; break;
				case ValuePaintBand.Shadow: extremity = 0.7f; break;
				default: extremity = 0.45f; break;
			}
			return Mathf.Clamp01(0.75f * extremity + 0.25f * Safe01(edgeMag01, 0.15f));
		}

		/// <summary>Clamp01 that also absorbs NaN/Inf — Mathf.Clamp01 propagates NaN.</summary>
		static float Safe01(float v, float fallback) {
			if (!float.IsFinite(v)) return Mathf.Clamp01(fallback);
			return Mathf.Clamp01(v);
		}

		// Static so the Live path does not allocate per tick.
		static readonly float[] BandEdges = {
			DeterministicValuePaintAssist.ShadowMin,
			DeterministicValuePaintAssist.MidtoneMin,
			DeterministicValuePaintAssist.LightMin,
			DeterministicValuePaintAssist.HighlightMin,
		};

		/// <summary>
		/// Prior-derived uncertainty: band-boundary proximity plus edge busyness. Mid-band flat
		/// patches are confident; boundary or high-edge samples are not.
		/// </summary>
		public static float UncertaintyFromPrior(float luminance01, float edgeMag01) {
			float lum = Safe01(luminance01, 0.5f);
			float nearest = 1f;
			for (int i = 0; i < BandEdges.Length; i++) {
				float d = Mathf.Abs(lum - BandEdges[i]);
				if (d < nearest) nearest = d;
			}
			// 0 distance → boundary → high uncertainty; ≥0.12 → settled inside a band.
			float boundary = 1f - Mathf.Clamp01(nearest / 0.12f);
			return Mathf.Clamp(0.18f + 0.5f * boundary + 0.25f * Safe01(edgeMag01, 0.15f), 0.05f, 0.95f);
		}
	}
}
