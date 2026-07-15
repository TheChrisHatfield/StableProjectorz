using UnityEngine;

namespace spz {

	/// <summary>Optional stroke / brush context for proposal generation (Spec R2).</summary>
	public struct ValuePaintStrokeState {
		public float BrushWidth01;
		public float Opacity01;
		public float BlendStrength01;
		public bool HasBrushHints;

		public static ValuePaintStrokeState FromBrushHints(float brushWidth01, float opacity01, float blendStrength01) {
			return new ValuePaintStrokeState {
				BrushWidth01 = Mathf.Clamp01(brushWidth01),
				Opacity01 = Mathf.Clamp01(opacity01),
				BlendStrength01 = Mathf.Clamp01(blendStrength01),
				HasBrushHints = true,
			};
		}
	}

}
