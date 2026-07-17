using UnityEngine;

namespace spz {

	/// <summary>
	/// T6 — sample local canvas/value/edge features for assist (Spec R2 input side).
	/// Feeds the locked 7-float T9 layout; depth/normal optional later.
	/// </summary>
	public static class StrokeFeatureExtractor {

		/// <summary>Build T9 features from a flat color patch (row-major if width known).</summary>
		public static void ExtractFromColors(Color[] patch, float[] dst7, int width = 0) {
			if (dst7 == null || dst7.Length < ValuePaintFeatureBuilder.FeatureDim)
				throw new System.ArgumentException("dst7");
			if (patch == null || patch.Length == 0) {
				ValuePaintFeatureBuilder.FromLuminance(0.5f, dst7);
				return;
			}

			int n = patch.Length;
			int w = width > 0 ? width : GuessWidth(n);
			int h = Mathf.Max(1, n / Mathf.Max(1, w));
			if (w * h > n)
				h = Mathf.Max(1, n / w);

			float[] hist = new float[5];
			float sumLum = 0f;
			float edgeAcc = 0f;
			int edgeCount = 0;

			for (int i = 0; i < n; i++) {
				float lum = DeterministicValuePaintAssist.Luminance01(patch[i]);
				sumLum += lum;
				hist[(int)DeterministicValuePaintAssist.BandFromLuminance(lum)] += 1f;

				int x = i % w;
				int y = i / w;
				if (x + 1 < w && i + 1 < n) {
					float r = DeterministicValuePaintAssist.Luminance01(patch[i + 1]);
					edgeAcc += Mathf.Abs(lum - r);
					edgeCount++;
				}
				if (y + 1 < h) {
					int below = i + w;
					if (below < n) {
						float d = DeterministicValuePaintAssist.Luminance01(patch[below]);
						edgeAcc += Mathf.Abs(lum - d);
						edgeCount++;
					}
				}
			}

			float inv = 1f / n;
			dst7[0] = sumLum * inv;
			for (int b = 0; b < 5; b++)
				dst7[1 + b] = hist[b] * inv;
			dst7[6] = edgeCount > 0 ? Mathf.Clamp01((edgeAcc / edgeCount) * 2f) : 0.15f;
		}

		/// <summary>Sample a CPU-readable Texture2D region into features (copies pixels).</summary>
		public static bool TryExtractFromTexture(Texture2D tex, RectInt region, float[] dst7, out string error) {
			error = null;
			if (tex == null) {
				error = "texture null";
				return false;
			}
			if (dst7 == null || dst7.Length < ValuePaintFeatureBuilder.FeatureDim) {
				error = "dst7";
				return false;
			}
			if (tex.width < 1 || tex.height < 1) {
				error = "texture empty";
				return false;
			}
			int x = Mathf.Clamp(region.x, 0, tex.width - 1);
			int y = Mathf.Clamp(region.y, 0, tex.height - 1);
			int w = Mathf.Clamp(region.width, 1, tex.width - x);
			int h = Mathf.Clamp(region.height, 1, tex.height - y);
			Color[] px;
			try {
				px = tex.GetPixels(x, y, w, h);
			} catch (System.Exception e) {
				error = e.Message;
				return false;
			}
			ExtractFromColors(px, dst7, w);
			return true;
		}

		static int GuessWidth(int n) {
			int s = Mathf.RoundToInt(Mathf.Sqrt(n));
			return Mathf.Max(1, s);
		}
	}

}
