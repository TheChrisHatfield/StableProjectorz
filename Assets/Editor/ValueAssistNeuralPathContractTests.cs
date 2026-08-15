using NUnit.Framework;
using UnityEngine;
using spz;
using spz.MlpDecimacon;

/// <summary>
/// Pass D / brush-behavior B8 — the neural path must actually be the product path, and
/// deterministic may only act as fallback or prior input. Guards the "silently on
/// deterministic" and "prior overwrites neural" failure modes.
/// </summary>
public sealed class ValueAssistNeuralPathContractTests {

	[SetUp]
	public void SetUp() {
		DecimaconProductGate.ResetForTests();
		ValueAssistNeuralHealth.Reset();
	}

	// B8.5 — shipped weights must load and shape-validate, or we learn exactly why.
	[Test]
	public void ValueHeadsWeights_LoadAndShapeValidate() {
		bool ok = ValueHeadsWeightsDto.TryLoad(out var dto, out string error);
		Assert.That(ok, Is.True, "value heads failed to load: " + error);
		Assert.That(dto, Is.Not.Null);
		Assert.That(dto.Validate(out string bad), Is.True, "shape invalid: " + bad);
		Assert.That(dto.width, Is.GreaterThan(0));
		Assert.That(dto.feature_dim, Is.EqualTo(ValuePaintFeatureBuilder.FeatureDim));
	}

	// B8.5 — a truncated tensor must fail with the offending key, not load silently.
	[Test]
	public void ValueHeadsWeights_TruncatedTensor_FailsWithKey() {
		Assert.That(ValueHeadsWeightsDto.TryLoad(out var dto, out _), Is.True);
		dto.des_weight = new float[3];
		Assert.That(dto.Validate(out string bad), Is.False);
		Assert.That(bad, Does.Contain("des_weight"), bad);
	}

	[Test]
	public void ValueHeadsWeights_NonFiniteTensor_FailsWithKey() {
		Assert.That(ValueHeadsWeightsDto.TryLoad(out var dto, out _), Is.True);
		dto.cont_bias = new[] { 0.1f, float.NaN, 0.2f, 0.3f };
		Assert.That(dto.Validate(out string bad), Is.False);
		Assert.That(bad, Does.Contain("cont_bias"), bad);
	}

	// B8.6 — resolution is observable; no silent fallback.
	[Test]
	public void Factory_PublishesNeuralHealth() {
		var assist = ValuePaintAssistFactory.Create(preferNeural: true, out string which);
		Assert.That(assist, Is.Not.Null);
		if (assist is MlpDecimaconPaintAssist) {
			Assert.That(ValueAssistNeuralHealth.IsNeuralActive, Is.True, ValueAssistNeuralHealth.Describe());
			Assert.That(ValueAssistNeuralHealth.Reason, Is.Empty);
		} else {
			Assert.That(ValueAssistNeuralHealth.IsUnwantedFallback, Is.True, ValueAssistNeuralHealth.Describe());
			Assert.That(ValueAssistNeuralHealth.Reason, Is.Not.Empty, "fallback must carry a reason: " + which);
		}
	}

	[Test]
	public void Factory_NeuralOff_ReportsUserOff_NotFailure() {
		ValuePaintAssistFactory.Create(preferNeural: false, out _);
		Assert.That(ValueAssistNeuralHealth.Current,
			Is.EqualTo(ValueAssistNeuralHealth.State.NeuralOff), ValueAssistNeuralHealth.Describe());
		Assert.That(ValueAssistNeuralHealth.IsUnwantedFallback, Is.False);
	}

	// B8.4 — neural proposals must be tagged neural, never as deterministic.
	[Test]
	public void NeuralProposal_SourceTagsDecimacon_NotDeterministic() {
		if (!MlpDecimaconPaintAssist.TryCreate(out var dec, out string err))
			Assert.Ignore("decimacon unavailable: " + err);

		var p = dec.ProposeFromLuminance(0.72f);
		Assert.That(p.Source, Does.StartWith("mlp_decimacon"), p.Source);
		Assert.That(p.Source, Does.Not.Contain("DeterministicValuePaintAssist"), p.Source);
	}

	// B8.1/B8.3 — the neural path must produce usable, finite, in-range fields on its own.
	[Test]
	public void NeuralProposal_FieldsFiniteAndInRange_AcrossLuminanceSweep() {
		if (!MlpDecimaconPaintAssist.TryCreate(out var dec, out string err))
			Assert.Ignore("decimacon unavailable: " + err);

		for (float lum = 0f; lum <= 1.0001f; lum += 0.1f) {
			var p = dec.ProposeFromLuminance(Mathf.Clamp01(lum));
			Assert.That(p.BlendStrength01, Is.InRange(0f, 1f), "blend @" + lum);
			Assert.That(p.EdgeSoftness01, Is.InRange(0f, 1f), "edge @" + lum);
			Assert.That(p.BrushWidthHint01, Is.InRange(0f, 1f), "width @" + lum);
			Assert.That(p.OpacityHint01, Is.InRange(0f, 1f), "opacity @" + lum);
			Assert.That(p.MeanLuminance01, Is.EqualTo(Mathf.Clamp01(lum)).Within(1e-4f), "lum @" + lum);
			// A proposal that cannot move the value plane is useless to the brush.
			Assert.That(p.DesiredBin, Is.Not.EqualTo(p.CurrentBin), "no value step @" + lum);
		}
	}

	// B8.4 — no prior-fill suffix when every head output is finite (prior must not leak in).
	[Test]
	public void NeuralProposal_NoPriorFillSuffix_WhenHeadsHealthy() {
		if (!MlpDecimaconPaintAssist.TryCreate(out var dec, out string err))
			Assert.Ignore("decimacon unavailable: " + err);

		var p = dec.ProposeFromLuminance(0.5f);
		Assert.That(p.Source, Does.Not.Contain("+prior:"), p.Source);
	}

	// B8.2 — the prior conditions routing; it must vary, not sit on an old constant.
	[Test]
	public void TaskValueFromPrior_VariesByBand_NotConstant() {
		float mid = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.Midtone, 0.15f);
		float accent = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.AccentDark, 0.15f);
		float highlight = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.Highlight, 0.15f);

		Assert.That(accent, Is.GreaterThan(mid), "accent should outrank midtone");
		Assert.That(highlight, Is.GreaterThan(mid), "highlight should outrank midtone");
		Assert.That(mid, Is.Not.EqualTo(0.55f).Within(1e-6f), "must not be the old hardcoded 0.55");
		Assert.That(mid, Is.InRange(0f, 1f));
		Assert.That(accent, Is.InRange(0f, 1f));
	}

	[Test]
	public void TaskValueFromPrior_RisesWithEdgeEnergy() {
		float flat = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.Midtone, 0f);
		float busy = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.Midtone, 1f);
		Assert.That(busy, Is.GreaterThan(flat));
	}

	// B8.2 — band boundaries are the uncertain samples; band centres are not.
	[Test]
	public void UncertaintyFromPrior_HigherAtBandBoundary_ThanBandCentre() {
		float boundary = MlpDecimaconPaintAssist.UncertaintyFromPrior(DeterministicValuePaintAssist.MidtoneMin, 0.1f);
		float centre = MlpDecimaconPaintAssist.UncertaintyFromPrior(0.52f, 0.1f);
		Assert.That(boundary, Is.GreaterThan(centre), "boundary=" + boundary + " centre=" + centre);
		Assert.That(boundary, Is.InRange(0f, 1f));
		Assert.That(centre, Is.InRange(0f, 1f));
		Assert.That(centre, Is.Not.EqualTo(0.3f).Within(1e-6f), "must not be the old hardcoded 0.3");
	}

	// Mathf.Clamp01 propagates NaN, so the prior helpers must absorb it explicitly.
	[Test]
	public void UncertaintyFromPrior_HandlesNonFiniteSafely() {
		float u = MlpDecimaconPaintAssist.UncertaintyFromPrior(float.NaN, float.NaN);
		Assert.That(float.IsFinite(u), Is.True, "NaN leaked into uncertainty");
		Assert.That(u, Is.InRange(0f, 1f));

		float inf = MlpDecimaconPaintAssist.UncertaintyFromPrior(float.PositiveInfinity, float.NegativeInfinity);
		Assert.That(float.IsFinite(inf), Is.True, "Inf leaked into uncertainty");
		Assert.That(inf, Is.InRange(0f, 1f));
	}

	[Test]
	public void TaskValueFromPrior_HandlesNonFiniteSafely() {
		float t = MlpDecimaconPaintAssist.TaskValueFromPrior(ValuePaintBand.Midtone, float.NaN);
		Assert.That(float.IsFinite(t), Is.True, "NaN leaked into task value");
		Assert.That(t, Is.InRange(0f, 1f));
	}

	// The whole point of the prior: a non-finite sample must still yield a usable proposal.
	[Test]
	public void NeuralProposal_NonFiniteLuminance_StillFinite() {
		if (!MlpDecimaconPaintAssist.TryCreate(out var dec, out string err))
			Assert.Ignore("decimacon unavailable: " + err);

		var p = dec.ProposeFromLuminance(float.NaN);
		Assert.That(float.IsFinite(p.MeanLuminance01), Is.True);
		Assert.That(p.BlendStrength01, Is.InRange(0f, 1f));
		Assert.That(p.OpacityHint01, Is.InRange(0f, 1f));
	}

	// B8.7 — train parity: heads run on features7, so the fused body latent stays unused.
	[Test]
	public void ValueHeads_DoNotConsumeBodyLatent_TrainParityLock() {
		Assert.That(ValueHeadsRuntime.TryCreate(out var heads, out string err), Is.True, err);
		Assert.That(heads.UsesBodyLatent, Is.False,
			"value heads were trained as z = proj(features7); mixing fused breaks parity (B8.7)");

		var feat = new float[ValuePaintFeatureBuilder.FeatureDim];
		ValuePaintFeatureBuilder.FromLuminance(0.6f, feat);
		var zeros = new float[DecimaconDims.Width];
		var ones = new float[DecimaconDims.Width];
		for (int i = 0; i < ones.Length; i++) ones[i] = 1f;

		var a = heads.Forward(zeros, feat);
		var b = heads.Forward(ones, feat);
		Assert.That(b.DesiredBin, Is.EqualTo(a.DesiredBin), "fused must not change the head decision");
		Assert.That(b.Blend01, Is.EqualTo(a.Blend01).Within(1e-6f));
	}

	// LAVD lock — measured feedback only: a deterministic propose runs no Decimacon
	// forward, so it must never train the bandit as if one had run.
	[Test]
	public void DeterministicLivePredict_DoesNotTrainBandit() {
		bool prevEnabled = PaintTab_ValueAssistOptions.Enabled;
		bool prevNeural = PaintTab_ValueAssistOptions.UseNeural;
		bool prevLive = PaintTab_ValueAssistOptions.LivePredict;
		try {
			PaintTab_ValueAssistOptions.SetEnabled(true);
			PaintTab_ValueAssistOptions.SetUseNeural(false);
			PaintTab_ValueAssistOptions.SetLivePredict(true);
			ValuePaintLivePredictor.InvalidateAssist();
			DecimaconProductGate.ResetForTests(23);

			var alpha = new float[4];
			var beta = new float[4];
			for (int i = 0; i < 4; i++) {
				alpha[i] = DecimaconProductGate.Scheduler.GetAlpha((BanditArm)i);
				beta[i] = DecimaconProductGate.Scheduler.GetBeta((BanditArm)i);
			}

			// Arm will refuse (no workflow UI in tests) or succeed — either way the assist is
			// deterministic, so no bandit alpha/beta may move.
			ValuePaintLivePredictor.TryPredictFromSurface(new Color(0.42f, 0.4f, 0.44f, 1f), out _);

			for (int i = 0; i < 4; i++) {
				Assert.That(DecimaconProductGate.Scheduler.GetAlpha((BanditArm)i),
					Is.EqualTo(alpha[i]).Within(1e-6f), "alpha moved for arm " + (BanditArm)i);
				Assert.That(DecimaconProductGate.Scheduler.GetBeta((BanditArm)i),
					Is.EqualTo(beta[i]).Within(1e-6f), "beta moved for arm " + (BanditArm)i);
			}
		} finally {
			PaintTab_ValueAssistOptions.SetLivePredict(prevLive);
			PaintTab_ValueAssistOptions.SetUseNeural(prevNeural);
			PaintTab_ValueAssistOptions.SetEnabled(prevEnabled);
			ValuePaintLivePredictor.InvalidateAssist();
			DecimaconProductGate.ResetForTests();
		}
	}

	// Requiring value heads is deliberate: a body without heads cannot propose (B8.5).
	[Test]
	public void Runtime_RequireValueHeads_IsEnforced() {
		bool created = MlpDecimaconRuntime.TryCreate(out var rt, out string err, requireValueHeads: true);
		if (!created)
			Assert.That(err, Is.Not.Empty, "failure must explain itself");
		else
			Assert.That(rt.HasValueHeads, Is.True, "requireValueHeads must guarantee heads");
	}
}
