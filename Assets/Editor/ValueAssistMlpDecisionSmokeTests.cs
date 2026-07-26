using NUnit.Framework;
using spz;

/// <summary>
/// Pass D: factory prefers soil MlpDecimacon; never MultiHead.
/// </summary>
public sealed class ValueAssistMlpDecisionSmokeTests {

	[Test]
	public void Factory_Neural_PrefersDecimacon_NotMultiHead() {
		var assist = ValuePaintAssistFactory.Create(preferNeural: true, out string which);
		Assert.That(which, Does.Not.Contain("MlpValuePaintAssist"), which);
		Assert.That(which, Does.Not.Contain("MultiHead"), which);
		Assert.That(
			which.Contains("MlpDecimaconPaintAssist") || which.Contains("Deterministic"),
			which);
		if (which.Contains("MlpDecimaconPaintAssist"))
			Assert.That(assist, Is.InstanceOf<MlpDecimaconPaintAssist>());
	}

	[Test]
	public void SanitizeBrushWidth_StillLoopsIntoSpzContract() {
		Assert.That(ValuePaintProposalApplier.SanitizeBrushWidthHint01(0.42f), Is.EqualTo(0.42f).Within(1e-5f));
	}
}
