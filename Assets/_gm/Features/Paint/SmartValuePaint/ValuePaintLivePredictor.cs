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
		/// <summary>Band Live last armed as DesiredBin — a mid-stroke sample matching it is our own paint (B2.2a).</summary>
		static ValuePaintBand _lastArmedDesired = (ValuePaintBand)(-1);

		public static string LastAssistWhich => _assistWhich;
		public static ValuePaintProposal LastProposal { get; private set; }
		public static bool HasLastProposal { get; private set; }

		/// <summary>
		/// Why Live last refused to arm, or "" when Live is healthy or deliberately holding
		/// its arm (B2.2b). "Live is on and nothing happens" must always be explainable.
		/// </summary>
		public static string LastRefusalReason { get; private set; } = "";

		public static bool IsLiveActive =>
			PaintTab_ValueAssistOptions.Enabled && PaintTab_ValueAssistOptions.LivePredict;

		public static void InvalidateAssist() {
			_assist = null;
			_assistWhich = "";
			ClearLiveUiState();
		}

		/// <summary>
		/// Drop the last Live proposal / refusal so status cannot keep saying "Live A→B" after
		/// the tool left Paint (B2.2c). Keeps the assist instance so returning to Paint does not
		/// pay another factory resolve.
		/// </summary>
		public static void ClearLiveUiState() {
			HasLastProposal = false;
			LastProposal = default;
			_lastDesired = (ValuePaintBand)(-1);
			_lastLiveArmTime = -999f;
			_lastArmedDesired = (ValuePaintBand)(-1);
			LastRefusalReason = "";
		}

		/// <param name="strokeActive">True while the user is mid-stroke — enables the self-read hold (B2.2a).</param>
		public static bool TryPredictFromSurface(Color surfaceSample, out string reason, bool strokeActive = false) {
			bool ok = PredictCore(surfaceSample, strokeActive, out reason, out bool holding);
			// A hold is normal operation, not a fault: it must not light up the status line.
			LastRefusalReason = (ok || holding) ? "" : reason;
			return ok;
		}

		static bool PredictCore(Color surfaceSample, bool strokeActive, out string reason, out bool holding) {
			reason = "";
			holding = false;
			if (!IsLiveActive) {
				reason = "live off";
				return false;
			}
			if (!IsFiniteColor(surfaceSample)) {
				reason = "non-finite sample";
				return false;
			}
			float lum = DeterministicValuePaintAssist.Luminance01(surfaceSample);
			ValuePaintBand plane = DeterministicValuePaintAssist.BandFromLuminance(lum);
			// B2.2a — mid-stroke the accumulation already holds the paint laid earlier this frame.
			// A sample landing on the band we armed is the brush reading itself; stepping again
			// ratchets value away run-away. Hold the arm until the tip reaches different form.
			if (strokeActive && HasLastProposal && plane == _lastArmedDesired) {
				holding = true;
				reason = "hold: self-read";
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
			// Measured-feedback-only: a deterministic assist (neural off / decimacon unavailable)
			// runs no Decimacon forward, so the bandit must only observe the hitch — never a
			// fabricated ranForward sample (LAVD lock).
			bool neuralForward = _assist is MlpDecimaconPaintAssist;
			var proposal = _assist.ProposeFromColor(surfaceSample, default);
			// B2.2 — Rec.709 owns the plane we are standing on; the value heads own the step.
			// (Until 2026-08-16 DesiredBin was forced to the plane as a MultiHead-collapse
			// workaround; measured false for Decimacon, and it made every stroke a no-op.)
			proposal.CurrentBin = plane;
			proposal.MeanLuminance01 = lum;
			if (proposal.DesiredBin == plane)
				proposal.DesiredBin = DeterministicValuePaintAssist.DesireAdjacentValueStep(plane);
			proposal.StrokeRole = DeterministicValuePaintAssist.RoleForTransition(plane, proposal.DesiredBin);
			if (!float.IsFinite(proposal.OpacityHint01) || proposal.OpacityHint01 < 0.05f)
				proposal.OpacityHint01 = OpacityForPlane(plane);
			else
				proposal.OpacityHint01 = Mathf.Lerp(proposal.OpacityHint01, OpacityForPlane(plane), 0.65f);
			if (!ValuePaintProposalApplier.TryLiveArm(proposal, out reason)) {
				DecimaconProductGate.EndInference(lavd, DecimaconProductGate.ElapsedMs(sw), ranForward: neuralForward, accuracy: 0.4f);
				return false;
			}
			DecimaconProductGate.EndInference(lavd, DecimaconProductGate.ElapsedMs(sw), ranForward: neuralForward);
			LastProposal = proposal;
			HasLastProposal = true;
			_lastArmedDesired = proposal.DesiredBin;
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
