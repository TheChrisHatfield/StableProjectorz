using UnityEngine;

namespace spz {

	/// <summary>
	/// Live under-cursor value prediction for Value Assist.
	/// Samples surface color → <see cref="IValuePaintAssist"/> → soft ribbon arm (color-first).
	/// </summary>
	public static class ValuePaintLivePredictor {

		static IValuePaintAssist _assist;
		static string _assistWhich = "";
		static bool _assistNeural;
		static ValuePaintBand _lastDesired = (ValuePaintBand)(-1);
		static float _lastLiveArmTime = -999f;

		public static string LastAssistWhich => _assistWhich;
		public static ValuePaintProposal LastProposal { get; private set; }
		public static bool HasLastProposal { get; private set; }

		public static bool IsLiveActive =>
			PaintTab_ValueAssistOptions.Enabled && PaintTab_ValueAssistOptions.LivePredict;

		public static void InvalidateAssist() {
			_assist = null;
			_assistWhich = "";
			HasLastProposal = false;
			LastProposal = default;
			_lastDesired = (ValuePaintBand)(-1);
			// Allow the next band announce immediately after invalidate (neural/live toggle).
			_lastLiveArmTime = -999f;
		}

		public static bool TryPredictFromSurface(Color surfaceSample, out string reason) {
			reason = "";
			if (!IsLiveActive) {
				reason = "live off";
				return false;
			}
			if (!IsFiniteColor(surfaceSample)) {
				reason = "non-finite sample";
				return false;
			}
			EnsureAssist();
			if (_assist == null) {
				reason = "no assist";
				return false;
			}
			var proposal = _assist.ProposeFromColor(surfaceSample, default);
			if (!ValuePaintProposalApplier.TryLiveArm(proposal, out reason)) {
				// Do not leave a "live" UI proposal that was never armed (false success).
				return false;
			}
			LastProposal = proposal;
			HasLastProposal = true;
			return true;
		}

		static bool IsFiniteColor(Color c) {
			return float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b) && float.IsFinite(c.a);
		}

		static void EnsureAssist() {
			bool prefer = PaintTab_ValueAssistOptions.UseNeural;
			if (_assist != null && _assistNeural == prefer) return;
			_assist = ValuePaintAssistFactory.Create(preferNeural: prefer, out _assistWhich);
			_assistNeural = prefer;
			_lastDesired = (ValuePaintBand)(-1);
		}

		/// <summary>True when desired band changed and debounce elapsed — caller may refresh cursor tint.</summary>
		public static bool ShouldAnnounceBandChange(ValuePaintBand desired) {
			if (desired == _lastDesired) return false;
			// Do not consume the new band while debouncing — otherwise a skipped announce never retries.
			if (Time.unscaledTime - _lastLiveArmTime < 0.35f) return false;
			_lastDesired = desired;
			_lastLiveArmTime = Time.unscaledTime;
			return true;
		}
	}
}
