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

	// B2.2c — tool leave must restore Live soft-arm; refusing the next TryLiveArm alone left
	// opacity/hardness/color on the ribbon so Smudge/Erase felt VA-driven.
	[Test]
	public void ToolLeave_WiringRestoresLiveSoftArm_Source() {
		string applier = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "SmartValuePaint",
			"ValuePaintProposalApplier.cs"));
		Assert.That(applier, Does.Contain("LeaveLiveSoftArmIfToolIneligible"));
		Assert.That(applier, Does.Contain("IsLiveToolAndModeEligible"));
		Assert.That(applier, Does.Contain("EnsureLiveLeaveHooks"));
		Assert.That(applier, Does.Contain("OnDirectionToggleChanged += LeaveLiveSoftArmIfToolIneligible"));
		Assert.That(applier, Does.Contain("_Act_OnModeChanged += OnWorkflowModeMaybeLeftLive"));

		string painter = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "Inpaint",
			"Inpaint_MaskPainter.cs"));
		Assert.That(painter, Does.Contain("LeaveLiveSoftArmIfToolIneligible()"),
			"per-frame leave must cover SetIsOnWithoutNotify (no direction event)");
	}

	// B2.2d — ForcePaintMode on every color write yanked Smudge/Erase back to Paint when
	// Live restored the pre-Live color. User picks still force Paint; assist quiet must not.
	[Test]
	public void AssistColorWrite_DoesNotSubscribeForcePaintMode_Source() {
		string dir = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI",
			"SD_BrushRibbon_UI_Direction.cs"));
		Assert.That(dir, Does.Contain("_onUserAuthoredBrushColor += OnBrushColorUpdated_ForcePaintMode"));
		Assert.That(dir, Does.Not.Contain("_onBrushColorUpdated += OnBrushColorUpdated_ForcePaintMode"));

		string colors = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI",
			"BrushRibbon_UI_Colors.cs"));
		Assert.That(colors, Does.Contain("fromAssist: true"));
		Assert.That(colors, Does.Contain("_onUserAuthoredBrushColor?.Invoke"));
		Assert.That(colors, Does.Contain("if (!fromAssist)"));
	}

	// B2.2c — leaving the tool must be able to wipe Live status without killing the assist cache.
	[Test]
	public void ClearLiveUiState_DropsProposalKeepsAssistReady() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetLivePredict(false);
			ValuePaintLivePredictor.TryPredictFromSurface(new Color(0.5f, 0.5f, 0.5f, 1f), out _);
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.Not.Empty);

			ValuePaintLivePredictor.ClearLiveUiState();
			Assert.That(ValuePaintLivePredictor.HasLastProposal, Is.False);
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.Empty);
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
		}
	}

	// B2.2e — Live must stop driving size after a traditional size edit; lerp-after-adopt
	// snapped the dial toward the model hint (a number the user never assigned).
	[Test]
	public void LiveSizeSoftArm_StopsAfterUserEdit_Source() {
		string applier = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "SmartValuePaint",
			"ValuePaintProposalApplier.cs"));
		Assert.That(applier, Does.Contain("_liveSizeUserOverride"));
		Assert.That(applier, Does.Contain("if (_liveSizeUserOverride) return;"));
		Assert.That(applier, Does.Contain("IsUserResizingBrushNow"));
		Assert.That(applier, Does.Contain("changedBeforeFirstAssistWrite"));
	}

	// B2.2b — empty-texel / missing-target skips never reached TryPredictFromSurface, so Live ON
	// over unpainted mesh looked like silent Idle.
	[Test]
	public void SamplerSkip_PublishesRefusalWithoutClearingAssist() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetLivePredict(true);
			ValuePaintLivePredictor.InvalidateAssist();

			ValuePaintLivePredictor.NoteSamplerSkip("empty texel");
			Assert.That(ValuePaintLivePredictor.LastRefusalReason, Is.EqualTo("empty texel"));
			Assert.That(ValuePaintLivePredictor.HasLastProposal, Is.False,
				"a skip must not invent a proposal");
			Assert.That(ValuePaintLivePredictor.LastAssistWhich, Is.Empty,
				"a skip must not drop / recreate the assist cache");
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
		}
	}

	[Test]
	public void SamplerSkip_WiringFromMaskPainter_Source() {
		string painter = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "Inpaint",
			"Inpaint_MaskPainter.cs"));
		Assert.That(painter, Does.Contain("NoteSamplerSkip(\"empty texel\")"));
		Assert.That(painter, Does.Contain("NoteSamplerSkip(\"no paint target\")"));
		Assert.That(painter, Does.Contain("NoteSamplerSkip(\"gpu read error\")"));
	}

	// B3.4 — Accept drops the Live snapshot then demotes after the first stroke. Capture must
	// reset _lastLiveHardnessIx or the next Live tick treats Accept's hardness as a user override.
	[Test]
	public void LiveHardnessTracker_ResetsOnNewSnapshot_Source() {
		string applier = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "SmartValuePaint",
			"ValuePaintProposalApplier.cs"));
		Assert.That(applier, Does.Contain("_lastLiveHardnessIx = int.MinValue"));
		int capture = applier.IndexOf("static void CaptureUserBrushSnapshot_IfNeeded", System.StringComparison.Ordinal);
		int restore = applier.IndexOf("static void RestoreUserBrushSnapshot_IfHeld", System.StringComparison.Ordinal);
		Assert.That(capture, Is.GreaterThanOrEqualTo(0));
		Assert.That(restore, Is.GreaterThan(capture));
		string captureBody = applier.Substring(capture, restore - capture);
		Assert.That(captureBody, Does.Contain("_lastLiveHardnessIx = int.MinValue"),
			"new Live session must not inherit the previous session's hardness write");
		int tryAccept = applier.IndexOf("static bool TryAccept(ValuePaintProposal proposal, Color proposeBaseColor, bool useBrushAsBase", System.StringComparison.Ordinal);
		int afterAccept = applier.IndexOf("public static float SanitizeBrushWidthHint01", System.StringComparison.Ordinal);
		Assert.That(tryAccept, Is.GreaterThanOrEqualTo(0));
		Assert.That(afterAccept, Is.GreaterThan(tryAccept));
		string acceptBody = applier.Substring(tryAccept, afterAccept - tryAccept);
		Assert.That(acceptBody, Does.Contain("_lastLiveHardnessIx = int.MinValue"),
			"Accept must drop the pre-Accept Live hardness tracker before demote/resume");
	}

	// B2.2f — default OpacityInfluence is 1.0; adopt-then-lerp fully overwrote 1–0 key opacity.
	[Test]
	public void LiveOpacitySoftArm_StopsAfterUserEdit_Source() {
		string applier = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "SmartValuePaint",
			"ValuePaintProposalApplier.cs"));
		Assert.That(applier, Does.Contain("_liveOpacityUserOverride"));
		Assert.That(applier, Does.Contain("if (!_liveOpacityUserOverride)"));
		Assert.That(applier, Does.Contain("NotifyUserOpacityChanged"));

		string opacity = System.IO.File.ReadAllText(System.IO.Path.Combine(
			Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI",
			"BrushRibbon_UI_Opacity.cs"));
		Assert.That(opacity, Does.Contain("NotifyUserOpacityChanged"),
			"1–0 keys must notify Live so a same-frame Live write cannot swallow the edit");
	}
}
