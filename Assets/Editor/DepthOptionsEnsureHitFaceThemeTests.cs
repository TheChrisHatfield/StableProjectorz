using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Depth Options Compact labels clear TMP hits — buttons need EnsureSelectableHitFace under Nomad.
/// </summary>
public sealed class DepthOptionsEnsureHitFaceThemeTests {

	[Test]
	public void ThemeDepthOptionsMenu_EnsuresSelectableHitFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "Main Viewport", "LeftRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("void ThemeDepthOptionsMenu");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(2200, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(body, Does.Contain("ApplyBoundChromeSelectable(btn, t.controlBg, t.accent)"));
	}
}
