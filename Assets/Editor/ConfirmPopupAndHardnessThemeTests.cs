using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ConfirmPopupAndHardnessThemeTests {
	[Test]
	public void ConfirmPopup_ThemesHostAndBackgroundShells() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""_Core"", ""UI (reusable)"", ""Widgets and Gadgets"", ""UI_ConfirmPopup_YesNo"", ""ConfirmPopup_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ThemeShellImagesUnder""));
		Assert.That(src, Does.Contain(""RestoreBoundChromeUnder(transform)""));
	}

	[Test]
	public void BrushRibbon_ThemesHardnessColorsHitOnly() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Paint"", ""BrushRibbon_UI"", ""BrushRibbon_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ThemeContentSafeHitOnly(_hardness)""));
		Assert.That(src, Does.Contain(""ThemeContentSafeHitOnly(_colors)""));
	}
}
