using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 25: help tips overlay + footer catalogue/updates/welcome buttons BoundChrome under Nomad.
/// </summary>
public sealed class BoundChromePass25HelpTipsThemeTests {

	[Test]
	public void ViewportStatusText_SourceThemesHelpOverlayAndFooterButtons() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Viewport/Main Viewport/Viewport_StatusText.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeHelpTipsOverlay"));
		Assert.That(src, Does.Contain("ThemeFooterButton(_3dGenerators_catalogue_button"));
		Assert.That(src, Does.Contain("ThemeFooterButton(_openCheckForUpdates_button"));
		Assert.That(src, Does.Contain("ThemeFooterButton(_openWelcomeNovice_button"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_helpTipsPanel.transform)"));
		Assert.That(src, Does.Contain("IsHelpTipAccentLine"));
	}
}
