using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// AddonManager remember-toggle: Toggle.graphic must be wired before any BoundChrome sprite swap.
/// </summary>
public sealed class AddonManagerRememberToggleChromeTests {

	[Test]
	public void RememberToggle_SourceAssignsGraphicWithoutRoundingCheckmark() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int create = src.IndexOf("BuildRememberEnabledPreferenceRow", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		// Narrow to the remember-row builder that creates Background + Checkmark.
		int ck = src.IndexOf("new GameObject(\"Checkmark\")", create, System.StringComparison.Ordinal);
		Assert.That(ck, Is.GreaterThan(0));
		int graphic = src.IndexOf("tgl.graphic = ckI;", ck, System.StringComparison.Ordinal);
		Assert.That(graphic, Is.GreaterThan(0));
		int roundCk = src.IndexOf("ApplyRoundedControlSprite(ckI", ck, System.StringComparison.Ordinal);
		Assert.That(roundCk, Is.LessThan(0), "Checkmark must not be solid-squared before/after graphic assign");
		int roundBg = src.IndexOf("ApplyRoundedControlSprite(bgI", create, System.StringComparison.Ordinal);
		Assert.That(roundBg, Is.GreaterThan(0));
		Assert.That(roundBg, Is.LessThan(graphic));
	}
}
