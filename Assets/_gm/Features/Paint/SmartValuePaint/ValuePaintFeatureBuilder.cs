using UnityEngine;

namespace spz {

	/// <summary>
	/// Builds the locked T9 7-float feature vector from luminance / patch colors
	/// (matches <c>mlp-train-spec.md</c> + <c>value_map.py</c> bands).
	/// </summary>
	public static class ValuePaintFeatureBuilder {
		public const int FeatureDim = 7;

		public static void FromLuminance(float luminance01, float[] dst, float edgeMag01 = 0.15f) {
			if (dst == null || dst.Length < FeatureDim)
				throw new System.ArgumentException("dst");
			float lum = float.IsFinite(luminance01) ? Mathf.Clamp01(luminance01) : 0.5f;
			int band = (int)DeterministicValuePaintAssist.BandFromLuminance(lum);
			dst[0] = lum;
			for (int i = 0; i < 5; i++)
				dst[1 + i] = (i == band) ? 0.85f : 0.0375f;
			// renormalize hist softly
			float sum = 0f;
			for (int i = 0; i < 5; i++) sum += dst[1 + i];
			if (sum > 1e-6f) {
				for (int i = 0; i < 5; i++) dst[1 + i] /= sum;
			}
			dst[6] = Mathf.Clamp01(edgeMag01);
		}

		public static void FromColors(Color[] patch, float[] dst) {
			StrokeFeatureExtractor.ExtractFromColors(patch, dst);
		}
	}

}
