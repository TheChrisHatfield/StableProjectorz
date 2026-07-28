using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Command ribbon strip tabs: only the Button face may raycast — dividers/labels steal clicks under Nomad.
/// </summary>
public sealed class CommandRibbonStripRaycastThemeTests {

	[Test]
	public void ThemeStripTabCell_SourceClearsNonFaceRaycasts() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/RightPanel/CommandRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearStripTabNonFaceRaycasts"));
		Assert.That(src, Does.Contain("HideMonolithOverlaysUnder"));
		Assert.That(src, Does.Contain("never on Restore SPZ leave"));
		int theme = src.IndexOf("void ThemeStripTabCell", System.StringComparison.Ordinal);
		Assert.That(theme, Is.GreaterThan(0));
		int leave = src.IndexOf("if (!recolorChrome)", theme, System.StringComparison.Ordinal);
		int nomad = src.IndexOf("Color fill = FlatStripTabFill", theme, System.StringComparison.Ordinal);
		Assert.That(leave, Is.GreaterThan(0));
		Assert.That(nomad, Is.GreaterThan(leave));
		string leaveBody = src.Substring(leave, nomad - leave);
		Assert.That(leaveBody, Does.Not.Contain("ClearStripTabNonFaceRaycasts"));
		Assert.That(src.IndexOf("ClearStripTabNonFaceRaycasts(cell)", nomad, System.StringComparison.Ordinal), Is.GreaterThan(0));
	}

	[Test]
	public void SettingsLauncher_SourceClearsChildGraphicRaycasts() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Settings/Settings_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ThemeFlatLauncherButton", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("GetComponentsInChildren<Graphic>"));
		Assert.That(body, Does.Contain("raycastTarget = false"));
	}
}
