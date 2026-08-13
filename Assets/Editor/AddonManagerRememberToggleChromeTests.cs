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
		int create = src.IndexOf("GameObject BuildRememberEnabledPreferenceRow(", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		int next = src.IndexOf("void EnsureRememberRowTooltip(", create, System.StringComparison.Ordinal);
		string body = src.Substring(create, next - create);
		Assert.That(body, Does.Contain("new GameObject(\"Checkmark\")"));
		Assert.That(body, Does.Contain("tgl.graphic = ckI;"));
		Assert.That(body, Does.Not.Contain("ApplyRoundedControlSprite(ckI"),
			"Checkmark must not be solid-squared before/after graphic assign");
		Assert.That(body, Does.Contain("bgI.sprite = UiRuntimeSprites.SolidRect"),
			"Button face must stay a hard rectangle (SolidRect), not a rounded capsule.");
		Assert.That(body, Does.Contain("UiRuntimeSprites.CircleFilled"),
			"Remember checkmark must assign CircleFilled or ON is invisible.");
	}
}
