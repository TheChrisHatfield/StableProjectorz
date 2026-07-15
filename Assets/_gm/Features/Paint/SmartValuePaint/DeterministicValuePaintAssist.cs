using UnityEngine;

namespace spz {

	/// <summary>
	/// Deterministic value-band stub (Task 3). Quantizes Rec.709 luminance into five bands
	/// and builds parameter hints. Never writes to layer Content / UV masks.
	/// Spec: smart-value-paint R1–R2; R5 MLP may later implement <see cref="IValuePaintAssist"/> behind the same API.
	/// </summary>
	public sealed class DeterministicValuePaintAssist : IValuePaintAssist {

		public const float HighlightMin = 0.85f;
		public const float LightMin = 0.65f;
		public const float MidtoneMin = 0.40f;
		public const float ShadowMin = 0.20f;

		public static float Luminance01(Color c) {
			float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
			if (!float.IsFinite(lum))
				return 0.5f;
			return Mathf.Clamp01(lum);
		}

		public static ValuePaintBand BandFromLuminance(float luminance01) {
			if (!float.IsFinite(luminance01))
				return ValuePaintBand.Midtone;
			float l = Mathf.Clamp01(luminance01);
			if (l >= HighlightMin) return ValuePaintBand.Highlight;
			if (l >= LightMin) return ValuePaintBand.Light;
			if (l >= MidtoneMin) return ValuePaintBand.Midtone;
			if (l >= ShadowMin) return ValuePaintBand.Shadow;
			return ValuePaintBand.AccentDark;
		}

		public ValuePaintProposal ProposeFromLuminance(float luminance01, ValuePaintStrokeState strokeState = default) {
			float lum = float.IsFinite(luminance01) ? Mathf.Clamp01(luminance01) : 0.5f;
			ValuePaintBand current = BandFromLuminance(lum);
			ValuePaintBand desired = DesireOneStepTowardMid(current);
			ValuePaintStrokeRole role = RoleForTransition(current, desired);

			float blend = strokeState.HasBrushHints ? strokeState.BlendStrength01 : DefaultBlend(current, desired);
			float edgeSoft = DefaultEdgeSoftness(role);
			float width = strokeState.HasBrushHints ? strokeState.BrushWidth01 : DefaultWidth(role);
			float opacity = strokeState.HasBrushHints ? strokeState.Opacity01 : DefaultOpacity(role, current);
			if (!float.IsFinite(blend)) blend = 0.55f;
			if (!float.IsFinite(edgeSoft)) edgeSoft = 0.5f;
			if (!float.IsFinite(width)) width = 0.5f;
			if (!float.IsFinite(opacity)) opacity = 0.55f;

			return new ValuePaintProposal {
				CurrentBin = current,
				DesiredBin = desired,
				BlendStrength01 = Mathf.Clamp01(blend),
				EdgeSoftness01 = Mathf.Clamp01(edgeSoft),
				BrushWidthHint01 = Mathf.Clamp01(width),
				OpacityHint01 = Mathf.Clamp01(opacity),
				StrokeRole = role,
				MeanLuminance01 = lum,
				Source = "DeterministicValuePaintAssist",
			};
		}

		public ValuePaintProposal ProposeFromColor(Color sample, ValuePaintStrokeState strokeState = default) {
			return ProposeFromLuminance(Luminance01(sample), strokeState);
		}

		public ValuePaintProposal ProposeFromColors(Color[] patch, ValuePaintStrokeState strokeState = default) {
			if (patch == null || patch.Length == 0)
				return ProposeFromLuminance(0.5f, strokeState);

			float sum = 0f;
			for (int i = 0; i < patch.Length; i++)
				sum += Luminance01(patch[i]);
			return ProposeFromLuminance(sum / patch.Length, strokeState);
		}

		static ValuePaintBand DesireOneStepTowardMid(ValuePaintBand current) {
			switch (current) {
				case ValuePaintBand.Highlight: return ValuePaintBand.Light;
				case ValuePaintBand.AccentDark: return ValuePaintBand.Shadow;
				case ValuePaintBand.Light: return ValuePaintBand.Midtone;
				case ValuePaintBand.Shadow: return ValuePaintBand.Midtone;
				default: return ValuePaintBand.Midtone;
			}
		}

		static ValuePaintStrokeRole RoleForTransition(ValuePaintBand current, ValuePaintBand desired) {
			if (current == desired) return ValuePaintStrokeRole.ReinforcePlane;
			// AccentDark role only when the desired bin is AccentDark (true accent stroke), not when leaving it toward mid.
			if (desired == ValuePaintBand.AccentDark)
				return ValuePaintStrokeRole.AccentDark;
			int dist = Mathf.Abs((int)current - (int)desired);
			if (dist >= 2) return ValuePaintStrokeRole.BridgePlanes;
			if (current == ValuePaintBand.Highlight || current == ValuePaintBand.Light)
				return ValuePaintStrokeRole.SoftenTransition;
			return ValuePaintStrokeRole.BlockIn;
		}

		static float DefaultBlend(ValuePaintBand current, ValuePaintBand desired) {
			return current == desired ? 0.55f : 0.7f;
		}

		static float DefaultEdgeSoftness(ValuePaintStrokeRole role) {
			switch (role) {
				case ValuePaintStrokeRole.SoftenTransition: return 0.75f;
				case ValuePaintStrokeRole.AccentDark: return 0.25f;
				case ValuePaintStrokeRole.BridgePlanes: return 0.55f;
				case ValuePaintStrokeRole.ReinforcePlane: return 0.4f;
				default: return 0.5f;
			}
		}

		static float DefaultWidth(ValuePaintStrokeRole role) {
			switch (role) {
				case ValuePaintStrokeRole.BlockIn: return 0.7f;
				case ValuePaintStrokeRole.AccentDark: return 0.35f;
				case ValuePaintStrokeRole.SoftenTransition: return 0.55f;
				default: return 0.5f;
			}
		}

		static float DefaultOpacity(ValuePaintStrokeRole role, ValuePaintBand current) {
			if (role == ValuePaintStrokeRole.BlockIn) return 0.65f;
			if (role == ValuePaintStrokeRole.AccentDark) return 0.8f;
			if (current == ValuePaintBand.Highlight) return 0.45f;
			return 0.55f;
		}
	}

}
