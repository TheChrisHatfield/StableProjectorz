using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Smudge tuning from Paint tab → Tool Options → Brush options (strength, angle, sampling).
	/// Decoupled from <see cref="SD_WorkflowOptionsRibbon_UI"/> layout so smudge lives with other brush options.
	/// </summary>
	public static class PaintTab_SmudgeBrushOptions {
		static float _strength01 = 1f;
		static float _angleDeg;
		/// <summary>0 = isotropic neighbor mix; higher = prefer neighbors close to the center texel on the surface (luminance-weighted RGB + alpha in compute).</summary>
		static float _colorMixSimilarity01;
		/// <summary>Integer grid radius in UV texel steps between neighbor taps (1–4). Larger = more spatial samples (still scaled by kernel spacing in the smudge compute pass).</summary>
		static int _neighborGridRadius = 2;
		/// <summary>When false (default), smudge on paint layers samples only layer pixels (and multi-layer “under” from other layers / scene buffer), not generated mesh UV accumulation — avoids pulling UvPaintedBrush / bake icon through transparent strokes.</summary>
		static bool _includeUvMeshInLayerSmudge;

		/// <summary>Fired after any stored value changes (including from API). Read from properties.</summary>
		public static event Action Changed;

		public static float Strength01 => _strength01;
		public static float AngleDeg => _angleDeg;
		public static float ColorMixSimilarity01 => _colorMixSimilarity01;
		public static int NeighborGridRadius => _neighborGridRadius;
		public static bool IncludeUvMeshInLayerSmudge => _includeUvMeshInLayerSmudge;

		static void RaiseChanged() => Changed?.Invoke();

		public static void SetStrength01(float v) {
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _strength01)) return;
			_strength01 = c;
			RaiseChanged();
		}

		public static void SetAngleDeg(float v) {
			float c = Mathf.Clamp(v, 0f, 360f);
			if (Mathf.Approximately(c, _angleDeg)) return;
			_angleDeg = c;
			RaiseChanged();
		}

		public static void SetColorMixSimilarity01(float v) {
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _colorMixSimilarity01)) return;
			_colorMixSimilarity01 = c;
			RaiseChanged();
		}

		public static void SetNeighborGridRadius(int r) {
			int c = Mathf.Clamp(r, 1, 4);
			if (c == _neighborGridRadius) return;
			_neighborGridRadius = c;
			RaiseChanged();
		}

		public static void SetIncludeUvMeshInLayerSmudge(bool v) {
			if (v == _includeUvMeshInLayerSmudge) return;
			_includeUvMeshInLayerSmudge = v;
			RaiseChanged();
		}

		public static bool TrySetStrengthFromApi(float v) {
			if (!float.IsFinite(v)) return false;
			SetStrength01(v);
			return true;
		}

		public static bool TrySetAngleFromApi(float v) {
			if (!float.IsFinite(v)) return false;
			SetAngleDeg(v);
			return true;
		}

		public static bool TrySetColorMixSimilarityFromApi(float v) {
			if (!float.IsFinite(v)) return false;
			SetColorMixSimilarity01(v);
			return true;
		}

		public static bool TrySetNeighborGridRadiusFromApi(int r) {
			if (r < 1 || r > 4) return false;
			SetNeighborGridRadius(r);
			return true;
		}

		public static bool TrySetIncludeUvMeshInLayerSmudgeFromApi(bool v) {
			SetIncludeUvMeshInLayerSmudge(v);
			return true;
		}
	}
}
