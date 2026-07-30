using UnityEngine;
using spz.MlpDecimacon;

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
			var lavd = DecimaconProductGate.BeginLive();
			if (!DecimaconProductGate.LastRunForward) {
				reason = "lavd skip:" + (DecimaconProductGate.LastSkipReason ?? "ultra_lean");
				return false;
			}
			EnsureAssist();
			if (_assist == null) {
				reason = "no assist";
				return false;
			}
			var sw = DecimaconProductGate.StartTimer();
			var proposal = _assist.ProposeFromColor(surfaceSample, default);
			float lum = DeterministicValuePaintAssist.Luminance01(surfaceSample);
			ValuePaintBand plane = DeterministicValuePaintAssist.BandFromLuminance(lum);
			proposal.CurrentBin = plane;
			proposal.DesiredBin = plane;
			proposal.MeanLuminance01 = lum;
			proposal.StrokeRole = ValuePaintStrokeRole.ReinforcePlane;
			if (!float.IsFinite(proposal.OpacityHint01) || proposal.OpacityHint01 < 0.05f)
				proposal.OpacityHint01 = OpacityForPlane(plane);
			else
				proposal.OpacityHint01 = Mathf.Lerp(proposal.OpacityHint01, OpacityForPlane(plane), 0.65f);
			if (!ValuePaintProposalApplier.TryLiveArm(proposal, out reason)) {
				DecimaconProductGate.EndInference(lavd, DecimaconProductGate.ElapsedMs(sw), ranForward: true, accuracy: 0.4f);
				return false;
			}
			DecimaconProductGate.EndInference(lavd, DecimaconProductGate.ElapsedMs(sw), ranForward: true);
			LastProposal = proposal;
			HasLastProposal = true;
			return true;
		}

		static float OpacityForPlane(ValuePaintBand plane) {
			switch (plane) {
				case ValuePaintBand.Highlight: return 0.38f;
				case ValuePaintBand.Light: return 0.52f;
				case ValuePaintBand.Midtone: return 0.65f;
				case ValuePaintBand.Shadow: return 0.78f;
				case ValuePaintBand.AccentDark: return 0.88f;
				default: return 0.6f;
			}
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

		public static bool ShouldAnnounceBandChange(ValuePaintBand desired) {
			if (desired == _lastDesired) return false;
			if (Time.unscaledTime - _lastLiveArmTime < 0.35f) return false;
			_lastDesired = desired;
			_lastLiveArmTime = Time.unscaledTime;
			return true;
		}
	}
}
