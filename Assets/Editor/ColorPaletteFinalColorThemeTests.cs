using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ColorPaletteFinalColorThemeTests {
	[Test]
	public void ColorPalette_ThemesFinalColorHitFaceWithoutSolidSquare_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)",
			"Widgets and Gadgets", "ColorPalette", "ColorPalette_Panel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyThemeTokens", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(_finalColor_button)"));
		Assert.That(body, Does.Contain("ClearNonFaceRaycastsForTheme(_finalColor_button)"));
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeSelectable(_finalColor_button"));
	}
}
