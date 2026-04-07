using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Smudge strength / angle set from Paint tab → Tool Options → Brush options.
	/// Decoupled from <see cref="SD_WorkflowOptionsRibbon_UI"/> layout so smudge tuning lives with other brush options.
	/// </summary>
	public static class PaintTab_SmudgeBrushOptions {
		static float _strength01 = 1f;
		static float _angleDeg;

		/// <summary>Fired after <see cref="Strength01"/> or <see cref="AngleDeg"/> changes (including from API). Read current values from properties.</summary>
		public static event Action Changed;

		/// <summary>0–1; multiplied with brush opacity when applying smudge.</summary>
		public static float Strength01 => _strength01;

		/// <summary>Smear direction in degrees (compute <c>_SmudgeAngleRad</c>).</summary>
		public static float AngleDeg => _angleDeg;

		static void RaiseChanged() => Changed?.Invoke();

		/// <summary>Updates strength; no-op if clamped value is unchanged. Raises <see cref="Changed"/> when the stored value changes.</summary>
		public static void SetStrength01(float v) {
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _strength01)) return;
			_strength01 = c;
			RaiseChanged();
		}

		/// <summary>Updates angle (0–360°); no-op if clamped value is unchanged. Raises <see cref="Changed"/> when the stored value changes.</summary>
		public static void SetAngleDeg(float v) {
			float c = Mathf.Clamp(v, 0f, 360f);
			if (Mathf.Approximately(c, _angleDeg)) return;
			_angleDeg = c;
			RaiseChanged();
		}

		/// <summary>Add-on / JSON-RPC: smudge mix strength 0–1.</summary>
		public static bool TrySetStrengthFromApi(float v) {
			if (!float.IsFinite(v)) return false;
			SetStrength01(v);
			return true;
		}

		/// <summary>Add-on / JSON-RPC: smear angle in degrees.</summary>
		public static bool TrySetAngleFromApi(float v) {
			if (!float.IsFinite(v)) return false;
			SetAngleDeg(v);
			return true;
		}
	}
}
