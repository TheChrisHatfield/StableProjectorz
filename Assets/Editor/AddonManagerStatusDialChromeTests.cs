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
	public void PreferencesShowInRibbon_DoesNotUseThemeCheckboxToggle() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeShowInRibbonCheckbox"),
			"Show-in-Ribbon must use ThemeShowInRibbonCheckbox (no SolidSquare stretch).");
		int themeItem = src.IndexOf("void ThemeAddonListItem(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static Transform FindChildRecursive(", themeItem, System.StringComparison.Ordinal);
		string body = src.Substring(themeItem, next - themeItem);
		Assert.That(body, Does.Not.Contain("ThemeCheckboxToggle(ribbonToggle"),
			"ThemeCheckboxToggle on ShowInRibbonToggle stretches the face into a green capsule over dials/names.");
		Assert.That(body, Does.Contain("ThemeShowInRibbonCheckbox(ribbonToggle"));
		Assert.That(src, Does.Contain("Viewport Gen Art dock only"),
			"RibbonOnlyFullscreen prefs must show dock-only copy without a N/A checkbox.");
	}

	[Test]
	public void PreferencesBody_LayoutDoesNotFightVerticalLayoutGroup() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		int create = src.IndexOf("void CreateAddonListItem(", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		int prefs = src.IndexOf("PreferencesBody", create, System.StringComparison.Ordinal);
		Assert.That(prefs, Is.GreaterThan(create));
		// Top-stretch anchors on PreferencesBody stacked the row over HeaderRow dial/name.
		Assert.That(src.Substring(prefs, 500), Does.Not.Contain("anchorMin = new Vector2(0f, 1f)"),
			"PreferencesBody must not use fixed top anchors under the item VLG.");
		Assert.That(src, Does.Contain("verticalLayout.childControlHeight = false"));
		Assert.That(src, Does.Contain("prefsBodyHLG.childControlHeight = false"));
		Assert.That(src, Does.Contain("horizontalLayout.childControlHeight = false"));
		// ThemeShowInRibbonCheckbox must not rewrite left anchors (fights HLG).
		int themeCb = src.IndexOf("static void ThemeShowInRibbonCheckbox(", System.StringComparison.Ordinal);
		int themeEnd = src.IndexOf("static void LockStatusDialLayout(", themeCb, System.StringComparison.Ordinal);
		string themeBody = src.Substring(themeCb, themeEnd - themeCb);
		Assert.That(themeBody, Does.Not.Contain("anchorMin"),
			"Show-in-Ribbon theming must leave HLG-owned anchors alone.");
	}

	[Test]
	public void ApplyThemeTokens_SnapshotsLayoutGroupsBeforeNomadPads() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int panelVlg = src.IndexOf("panelVlg", apply, System.StringComparison.Ordinal);
		Assert.That(panelVlg, Is.GreaterThan(apply));
		int snapshot = src.IndexOf("ApplyScaledLayoutGroup(panelVlg)", panelVlg, System.StringComparison.Ordinal);
		int padWrite = src.IndexOf("panelVlg.padding = new RectOffset", panelVlg, System.StringComparison.Ordinal);
		Assert.That(snapshot, Is.GreaterThan(0), "Must snapshot panel VLG before Nomad absolute pads.");
		Assert.That(padWrite, Is.GreaterThan(snapshot), "Absolute Nomad padding must follow ApplyScaledLayoutGroup snapshot.");
		Assert.That(src.IndexOf("ApplyScaledLayoutGroup(headerHlg)", apply, System.StringComparison.Ordinal), Is.GreaterThan(0));
		Assert.That(src.IndexOf("ApplyScaledLayoutGroup(listVlg)", apply, System.StringComparison.Ordinal), Is.GreaterThan(0));
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
