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
		Assert.That(src, Does.Contain("Dividers are visual only"));
		int clear = src.IndexOf("static void ClearStripTabNonFaceRaycasts", System.StringComparison.Ordinal);
		Assert.That(clear, Is.GreaterThan(0));
		string body = src.Substring(clear, System.Math.Min(700, src.Length - clear));
		Assert.That(body, Does.Contain("g.raycastTarget = false"));
		Assert.That(body, Does.Contain("raycastTarget = true"));
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
