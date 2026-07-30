using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Value Assist has no Size dial; width hints loop into canonical BrushRibbon_UI_Size.
/// </summary>
public sealed class ValueAssistBrushSizeTraditionalTests {

	[Test]
	public void SizeInfluence_HasNoUiDial_AndReportsFullApply() {
		PaintTab_ValueAssistOptions.SetSizeInfluence01(0f);
		Assert.That(PaintTab_ValueAssistOptions.SizeInfluence01, Is.EqualTo(1f),
			"No Size dial — VA always applies width hint into SPZ size at full strength.");
	}

	[Test]
	public void SanitizeBrushWidthHint_ClampsAndFallsBack() {
		Assert.That(ValuePaintProposalApplier.SanitizeBrushWidthHint01(0.42f), Is.EqualTo(0.42f).Within(1e-5f));
		Assert.That(ValuePaintProposalApplier.SanitizeBrushWidthHint01(-1f), Is.EqualTo(0f));
		Assert.That(ValuePaintProposalApplier.SanitizeBrushWidthHint01(2f), Is.EqualTo(1f));
		Assert.That(ValuePaintProposalApplier.SanitizeBrushWidthHint01(float.NaN), Is.EqualTo(0.5f));
	}

	[Test]
	public void SoftnessToHardness_StillMapsIndependentlyOfSize() {
		Assert.That(ValuePaintProposalApplier.Softness01ToHardnessIx(0.9f), Is.EqualTo(0));
		Assert.That(ValuePaintProposalApplier.Softness01ToHardnessIx(0.5f), Is.EqualTo(1));
		Assert.That(ValuePaintProposalApplier.Softness01ToHardnessIx(0.1f), Is.EqualTo(2));
	}

	[Test]
	public void PanelSource_HasNoSizeDial_ButApplierWritesSpzSize() {
		string panelPath = System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Features/Paint/PaintTab/PaintTab_ValueAssistPanel_UI.cs");
		string applierPath = System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Features/Paint/SmartValuePaint/ValuePaintProposalApplier.cs");
		Assert.That(System.IO.File.Exists(panelPath), Is.True, panelPath);
		Assert.That(System.IO.File.Exists(applierPath), Is.True, applierPath);
		string panel = System.IO.File.ReadAllText(panelPath);
		string applier = System.IO.File.ReadAllText(applierPath);
		Assert.That(panel, Does.Not.Contain("MakeValueDial(_knobRow.transform, \"Size\""));
		Assert.That(panel, Does.Not.Contain("_sizeDial"));
		Assert.That(applier, Does.Contain("SoftArmBrushWidthIntoSpzSize"));
		Assert.That(applier, Does.Contain("sd.SetBrushSize"));
		Assert.That(applier, Does.Contain("BrushRibbon_UI_Size"));
	}
}
