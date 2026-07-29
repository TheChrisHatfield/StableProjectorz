using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 19: CTRL tab — ControlNet collapsed headers + circle dial values stay visible under Nomad.
/// </summary>
public sealed class BoundChromePass19ControlNetTitleDialVisibilityTests {

	[Test]
	public void ControlNetUnit_TitleUsesBoundChromeTmpNotStripLabel() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/Controlnet/ControlNetUnit_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeControlNetUnitTitle"));
		Assert.That(src, Does.Contain("ApplyBoundChromeTmp(_mainHeader"));
		Assert.That(src, Does.Not.Contain("ApplyBoundChromeStripLabelTmp(_mainHeader"));
		Assert.That(src, Does.Contain("characterSpacing = 2f"));
	}

	[Test]
	public void CircleSlider_SourceHidesOuterBoxAndUsesTextPrimary() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/_Core/UI (reusable)/Widgets and Gadgets/Slider/CircleSlider_Snapping_UI.cs"));
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("public void ApplyThemeTokens", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		string body = src.Substring(apply, System.Math.Min(1800, src.Length - apply));
		Assert.That(body, Does.Contain("HideAuthoredGraphicForTheme(img)"));
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp(_text, textPrimary"));
		Assert.That(body, Does.Not.Contain("ApplyRoundedControlSprite(img"));
	}
}
