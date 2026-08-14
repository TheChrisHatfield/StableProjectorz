using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ConfirmPopupAndHardnessThemeTests {
	[Test]
	public void ConfirmPopup_ThemesHostAndBackgroundShells() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)", "Widgets and Gadgets", "UI_ConfirmPopup_YesNo", "ConfirmPopup_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeShellImagesUnder"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(transform)"));
	}

	[Test]
	public void ConfirmPopup_ReShowInvokesPriorCancelAndGatesEscape_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)", "Widgets and Gadgets", "UI_ConfirmPopup_YesNo", "ConfirmPopup_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Prior cancel on re-Show"),
			"Re-entrant Show must invoke prior onNo so Addon Manager uninstall session cannot leak.");
		Assert.That(src, Does.Contain("if (!IsShowing) return"),
			"Escape must only cancel while the confirm dialog is visible.");
		Assert.That(src, Does.Contain("OnDestroy cancel"),
			"Destroy must run cancel cleanup so manager sort is restored.");
	}

	[Test]
	public void BrushRibbon_ThemesHardnessColorsHitOnly() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeContentSafeHitOnly(_hardness)"));
		Assert.That(src, Does.Contain("ThemeContentSafeHitOnly(_colors)"));
	}
}
