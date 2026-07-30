using NUnit.Framework;
using spz;
using UnityEngine;

public sealed class ColorPaletteHexCommitTests {

	[Test]
	public void TryParseHexColor_AcceptsRgbAndRrggbb() {
		Assert.That(ColorPalette_Panel_UI.TryParseHexColor("FF0000", out Color c1), Is.True);
		Assert.That(c1.r, Is.EqualTo(1f).Within(0.01f));
		Assert.That(c1.g, Is.EqualTo(0f).Within(0.01f));

		Assert.That(ColorPalette_Panel_UI.TryParseHexColor("#00FF00", out Color c2), Is.True);
		Assert.That(c2.g, Is.EqualTo(1f).Within(0.01f));

		Assert.That(ColorPalette_Panel_UI.TryParseHexColor("not-a-color", out _), Is.False);
		Assert.That(ColorPalette_Panel_UI.TryParseHexColor("", out _), Is.False);
	}
}
