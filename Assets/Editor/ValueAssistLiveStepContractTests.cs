using NUnit.Framework;
using UnityEngine;
using spz;
using spz.MlpDecimacon;

/// <summary>
/// brush-behavior B2.2 / B2.2a / B2.2b — Live must arm a real neural value step, must not
/// step off its own paint mid-stroke, and must never fail silently. Locks the 2026-08-16
/// removal of the plane-follow override (`DesiredBin = CurrentBin`) that made Live strokes
/// lay the value already under the tip.
/// </summary>
public sealed class ValueAssistLiveStepContractTests {

	[SetUp]
	public void SetUp() {
		DecimaconProductGate.ResetForTests();
		ValueAssistNeuralHealth.Reset();
	}

	// B2.2 — the load-bearing measurement behind dropping the override: the shipped Decimacon
	// value heads read the Rec.709 plane correctly and never collapse desired onto current.
	// If a retrained model ever regresses to collapse, this fails loudly instead of shipping
	// a Live mode that silently does nothing.
	[Test]
	public void DecimaconHeads_TrackPlaneAndProposeRealStep() {
		if (!MlpDecimaconPaintAssist.TryCreate(out var dec, out string err))
			Assert.Ignore("decimacon unavailable: " + err);

		int collapsed = 0;
		for (float lum = 0.05f; lum <= 0.96f; lum += 0.05f) {
			var plane = DeterministicValuePaintAssist.BandFromLuminance(lum);
			var p = dec.ProposeFromLuminance(lum);
			Assert.That(p.CurrentBin, Is.EqualTo(plane),
				"heads misread the plane at lum=" + lum.ToString("F2"));
			if (p.DesiredBin == p.CurrentBin) collapsed++;
		}
		Assert.That(collapsed, Is.EqualTo(0),
			"value heads collapsed desired→current at " + collapsed + " sample(s); "
			+ "Live would arm the value already under the tip (see B2.2 history)");
	}

	// B2.2 — the degenerate-step fallback must always produce a real change, so Live can never
	// arm a no-op even if the model does collapse.
	[Test]
	public void AdjacentStepFallback_NeverReturnsSameBand() {
		foreach (ValuePaintBand b in System.Enum.GetValues(typeof(ValuePaintBand))) {
			var step = DeterministicValuePaintAssist.DesireAdjacentValueStep(b);
			Assert.That(step, Is.Not.EqualTo(b), "no-op step for band " + b);
		}
	}

	// B2.2b — a genuine refusal must be reportable; "Live is on and nothing happens" is a bug
	// only visible if the reason survives the call.
	[Test]
	public void LiveRefusal_PublishesReason() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetLivePredict(false); // live off → deterministic refusal
			ValuePaintLivePredictor.InvalidateAssist();

			bool ok = ValuePaintLivePredictor.TryPredictFromSurface(
				new Color(0.42f, 0.4f, 0.44f, 1f), out string reason);

			Assert.That(ok, Is.False);
			Assert.That(reason, Is.Not.Empty);
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.EqualTo(reason),
				"refusal reason was not published for the status line");
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
		}
	}

	// B2.2b — invalidation clears the reason, so a stale refusal cannot haunt the panel after
	// Dismiss / Live toggle.
	[Test]
	public void InvalidateAssist_ClearsRefusalReason() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetLivePredict(false);
			ValuePaintLivePredictor.TryPredictFromSurface(new Color(0.5f, 0.5f, 0.5f, 1f), out _);
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.Not.Empty);

			ValuePaintLivePredictor.InvalidateAssist();
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.Empty);
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
		}
	}

	// B2.2a — the self-read hold may only engage once Live actually has an arm to hold.
	// Without a prior proposal a mid-stroke sample must fall through to normal evaluation,
	// otherwise starting a stroke could wedge Live off permanently.
	[Test]
	public void SelfReadHold_DoesNotEngageWithoutPriorProposal() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetLivePredict(true);
			ValuePaintLivePredictor.InvalidateAssist(); // clears HasLastProposal

			ValuePaintLivePredictor.TryPredictFromSurface(
				new Color(0.42f, 0.4f, 0.44f, 1f), out string reason, strokeActive: true);

			Assert.That(reason, Does.Not.Contain("hold: self-read"),
				"hold engaged with no prior arm — a stroke could wedge Live off");
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
		}
	}
}
