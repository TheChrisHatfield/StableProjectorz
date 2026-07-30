using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class LeftRibbonAddonHeaderEnsureThemeTests {
	[Test]
	public void LeftRibbon_WireframeAndDepEnsureHitFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "Main Viewport", "LeftRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(btn)"));
		int themeToggleIx = src.IndexOf("static void ThemeToggle(Toggle toggle");
		Assert.That(themeToggleIx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(themeToggleIx, System.Math.Min(600, src.Length - themeToggleIx));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(toggle)"));
	}

	[Test]
	public void AddonHeaderButton_EnsuresHitFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemeHeaderButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(500, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(button)"));
	}
}
