using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Paint-tab Value Assist / neural brush controls (Tool Options → Value Assist).
	/// Decoupled from ribbon layout — Propose/Accept and <see cref="ValuePaintProposalApplier"/> read here.
	/// </summary>
	public static class PaintTab_ValueAssistOptions {
		static bool _enabled = true;
		static bool _useNeural = true;
		static bool _applyHardness = true;
		static bool _livePredict = true;
		static float _blend01 = 1f;
		static float _sizeInfluence01 = 1f;
		static float _opacityInfluence01 = 1f;

		/// <summary>Fired after any stored value changes.</summary>
		public static event Action Changed;

		public static bool Enabled => _enabled;
		/// <summary>When true, prefer MLP weights; when false, force deterministic stub.</summary>
		public static bool UseNeural => _useNeural;
		public static bool ApplyHardness => _applyHardness;
		/// <summary>When true, sample under-cursor surface and soft-arm ribbon while hovering/painting.</summary>
		public static bool LivePredict => _livePredict;
		/// <summary>Multiplies proposal <see cref="ValuePaintProposal.BlendStrength01"/> on Accept (0 = no blend pull).</summary>
		public static float Blend01 => _blend01;
		/// <summary>0 = keep live brush size; 1 = use proposal width hint.</summary>
		public static float SizeInfluence01 => _sizeInfluence01;
		/// <summary>0 = keep live opacity; 1 = use proposal opacity×blend.</summary>
		public static float OpacityInfluence01 => _opacityInfluence01;

		static void RaiseChanged() => Changed?.Invoke();

		public static void SetEnabled(bool v) {
			if (v == _enabled) return;
			_enabled = v;
			if (!_enabled)
				ValuePaintProposalApplier.ClearArmed();
			RaiseChanged();
		}

		public static void SetUseNeural(bool v) {
			if (v == _useNeural) return;
			_useNeural = v;
			ValuePaintLivePredictor.InvalidateAssist();
			RaiseChanged();
		}

		public static void SetApplyHardness(bool v) {
			if (v == _applyHardness) return;
			_applyHardness = v;
			RaiseChanged();
		}

		public static void SetLivePredict(bool v) {
			if (v == _livePredict) return;
			_livePredict = v;
			if (!_livePredict) {
				ValuePaintLivePredictor.InvalidateAssist();
				// Soft live-arm is not Accept — drop it when Live turns off; keep user Accept arms.
				ValuePaintProposalApplier.ClearArmedIfLiveSoftArm();
			}
			RaiseChanged();
		}

		public static void SetBlend01(float v) {
			if (!float.IsFinite(v)) return;
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _blend01)) return;
			_blend01 = c;
			RaiseChanged();
		}

		public static void SetSizeInfluence01(float v) {
			if (!float.IsFinite(v)) return;
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _sizeInfluence01)) return;
			_sizeInfluence01 = c;
			RaiseChanged();
		}

		public static void SetOpacityInfluence01(float v) {
			if (!float.IsFinite(v)) return;
			float c = Mathf.Clamp01(v);
			if (Mathf.Approximately(c, _opacityInfluence01)) return;
			_opacityInfluence01 = c;
			RaiseChanged();
		}
	}
}
