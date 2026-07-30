using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class BrushRibbonHardnessColorsLeaveThemeTests {
	[Test]
	public void BrushRibbon_LeaveRestoresHardnessAndColorsRoots() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_hardness.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_colors.transform)"));
	}
}

public sealed class ColorPaletteBoundChromeThemeTests {
	[Test]
	public void ColorPalette_SubscribesThemeChangedAndThemesCommit() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)", "Widgets and Gadgets", "ColorPalette", "ColorPalette_Panel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeChanged += ApplyThemeTokens"));
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(_commitButton"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(transform)"));
	}
}
