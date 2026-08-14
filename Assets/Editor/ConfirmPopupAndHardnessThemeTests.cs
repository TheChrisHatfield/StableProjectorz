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
		Assert.That(src, Does.Contain("prior acts discarded, not cancelled"),
			"Re-entrant Show must discard prior acts silently — invoking onNo falsely reports Uninstall cancelled.");
		Assert.That(src, Does.Contain("if (!IsShowing) return"),
			"Escape must only cancel while the confirm dialog is visible.");
		Assert.That(src, Does.Contain("_suppressBackgroundDismissUntilPointerUp"),
			"Dimmer must ignore the same pointer that opened the dialog.");
		Assert.That(src, Does.Contain("ElevateForModalShow"),
			"Show must always Overlay-elevate ConfirmPopup (Settings + Addon Manager litmus).");
		Assert.That(src, Does.Contain("AbortAndRestoreUi"),
			"Must be able to abort a stuck elevated dimmer without invoking Yes/No.");
		Assert.That(src, Does.Contain("SuppressBackgroundMaxSec"),
			"Background suppress must time out to prevent full-UI lock.");
		Assert.That(src, Does.Contain("EnsureClickableLayout"),
			"Authored ConfirmPopup root is scale 0 — Show must stretch it for Yes/No.");
		Assert.That(src, Does.Not.Contain("Prior cancel on re-Show"),
			"Must not invoke prior onNo on re-Show (that was the Uninstall cancelled false positive).");
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
