using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Addon Manager status dials must tint without Flatten, snapshot under Nomad, and re-apply after Restore SPZ.
/// </summary>
public sealed class AddonManagerStatusDialChromeTests {

	[Test]
	public void ThemeAddonListItem_DoesNotFlattenStatusDialGraphics() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int themeItem = src.IndexOf("void ThemeAddonListItem(", System.StringComparison.Ordinal);
		Assert.That(themeItem, Is.GreaterThan(0));
		int nextMethod = src.IndexOf("static Transform FindChildRecursive(", themeItem, System.StringComparison.Ordinal);
		Assert.That(nextMethod, Is.GreaterThan(themeItem));
		string body = src.Substring(themeItem, nextMethod - themeItem);
		Assert.That(body, Does.Contain("TintStatusDialGraphic"),
			"Status dials must tint via TintStatusDialGraphic (snapshot + color, no Flatten).");
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeGraphic(ringImg"),
			"ApplyBoundChromeGraphic on CircleRing flattens dials into capsules.");
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeGraphic(fill"),
			"ApplyBoundChromeGraphic on Checkmark flattens dials into capsules.");
	}

	[Test]
	public void ApplyThemeTokens_ReappliesDialsAfterRestore() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ReapplyAuthoredStatusDialsAfterThemeRestore()"),
			"Leave/builtin path must re-tint dials after RestoreBoundChromeUnder so Nomad green does not stick.");
		Assert.That(src, Does.Contain("SnapshotAuthoredGraphicForTheme"),
			"Dial tints under Nomad must snapshot so Restore can unwind.");
	}

	[Test]
	public void PreferencesButton_SnapshotsSolidRectBeforeRoundedMark() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("AssignSolidFaceThenMarkRounded"),
			"Manager faces must assign SolidRect via AssignSolidFaceThenMarkRounded before markEligible.");
		int addBar = src.IndexOf("void AddBarButton(", System.StringComparison.Ordinal);
		Assert.That(addBar, Is.GreaterThan(0));
		int next = src.IndexOf("AddBarButton(headerObj.transform", addBar + 10, System.StringComparison.Ordinal);
		if (next < 0) next = src.IndexOf("\n\t\tGameObject CreateFilterToggle", addBar, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(addBar));
		string body = src.Substring(addBar, next - addBar);
		Assert.That(body, Does.Contain("AssignSolidFaceThenMarkRounded(img)"),
			"AddBarButton must not call ApplyRoundedControlSprite on a null-sprite Image.");
		Assert.That(body, Does.Not.Contain("ApplyRoundedControlSprite(img, markEligible: true)"),
			"Raw markEligible on AddBarButton Image must go through AssignSolidFaceThenMarkRounded.");
	}
}
