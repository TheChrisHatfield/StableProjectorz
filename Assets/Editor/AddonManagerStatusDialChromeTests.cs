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
	public void CloseButton_DoesNotUseCompactToolLabel() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		int close = src.IndexOf("_closePanel_button", apply, System.StringComparison.Ordinal);
		Assert.That(close, Is.GreaterThan(apply));
		int blockEnd = src.IndexOf("_installFromFile_button", close, System.StringComparison.Ordinal);
		string block = src.Substring(close, blockEnd - close);
		Assert.That(block, Does.Not.Contain("ApplyBoundChromeCompactToolLabelTmp"),
			"Close CompactToolLabel clips under Nomad like Uninstall/Disabled.");
		Assert.That(block, Does.Contain("ApplyBoundChromeTmp"));
	}

	[Test]
	public void UninstallLabel_DoesNotUseCompactToolLabel() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		int themeItem = src.IndexOf("void ThemeAddonListItem(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static Transform FindChildRecursive(", themeItem, System.StringComparison.Ordinal);
		string body = src.Substring(themeItem, next - themeItem);
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeCompactToolLabelTmp(removeLabel"),
			"Uninstall CompactToolLabel clips to UNINSTA□ under Nomad like Preferences did.");
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp(removeLabel"));
	}

	[Test]
	public void PreferencesShowInRibbon_UsesRingDialNotSquareFace() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeShowInRibbonDial"),
			"Show-in-Ribbon must use ThemeShowInRibbonDial (ring + fill, no square plate).");
		Assert.That(src, Does.Contain("CircleRing"),
			"Ribbon dial must use CircleRing like the enable status dial.");
		Assert.That(src, Does.Not.Contain("ThemeShowInRibbonCheckbox"),
			"Legacy square-face ThemeShowInRibbonCheckbox must be removed.");
		int themeItem = src.IndexOf("void ThemeAddonListItem(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static Transform FindChildRecursive(", themeItem, System.StringComparison.Ordinal);
		string body = src.Substring(themeItem, next - themeItem);
		Assert.That(body, Does.Not.Contain("ThemeCheckboxToggle(ribbonToggle"),
			"ThemeCheckboxToggle on ShowInRibbonToggle stretches the face into a green capsule over dials/names.");
		Assert.That(body, Does.Contain("ThemeShowInRibbonDial(ribbonToggle"));
		Assert.That(src, Does.Contain("Viewport Gen Art dock only"),
			"RibbonOnlyFullscreen prefs must show dock-only copy without a N/A checkbox.");
		Assert.That(src, Does.Contain("prefRowBg.color = Color.clear"),
			"Pref row must not paint a square/row plate under Host preferences.");
		Assert.That(src, Does.Contain("ribbonLabel.raycastTarget = true"),
			"RibbonOnly tip must hit the label when dial is hidden.");
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
		Assert.That(src, Does.Contain("prefsBodyVLG.childControlHeight = true"),
			"Prefs body must control child heights so Host preferences stacks under the section header.");
		Assert.That(src, Does.Contain("prefRowHLG.childControlHeight = false"),
			"Pref row HLG must not stretch the dial.");
		Assert.That(src, Does.Contain("PrefRow_ShowInRibbon"));
		Assert.That(src, Does.Contain("ApplyResponsivePrefsDropdownLayout"));
		Assert.That(src, Does.Contain("ResolveOrCaptureDesignFontPt(header"),
			"Responsive prefs must not stomp BoundChrome fontScale with bare fontSize writes.");
		Assert.That(src, Does.Contain("ResolveOrCaptureDesignFontPt(label"));
		Assert.That(src, Does.Contain("horizontalLayout.childControlHeight = false"),
			"HeaderRow HLG must not control height (protects status dial).");
		Assert.That(src, Does.Contain("Preferences ▾"));
		Assert.That(src, Does.Contain("verticalLayout.childControlHeight = true"),
			"Item VLG must assign HeaderRow + PreferencesBody heights or Host preferences overlays the name.");
		Assert.That(src, Does.Contain("contentLayout.childControlHeight = true"),
			"List content must size AddonItem rows to preferredHeight when prefs expand.");
		Assert.That(src, Does.Contain("verticalLayout.spacing = 8f"),
			"Gap between header and prefs body so Preferences/Uninstall are not clipped.");
		Assert.That(src, Does.Contain("headerLE.preferredHeight = 40f"),
			"Header must be tall enough for 28px buttons without prefs overlap.");
		Assert.That(src, Does.Contain("SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h)"),
			"Expanded item rect must sync with LayoutElement preferredHeight.");
		// ThemeShowInRibbonDial must not rewrite left anchors (fights HLG).
		int themeCb = src.IndexOf("static void ThemeShowInRibbonDial(", System.StringComparison.Ordinal);
		int themeEnd = src.IndexOf("static void LockStatusDialLayout(", themeCb, System.StringComparison.Ordinal);
		string themeBody = src.Substring(themeCb, themeEnd - themeCb);
		Assert.That(themeBody, Does.Not.Contain("anchorMin = Vector2.zero"),
			"Show-in-Ribbon theming must leave HLG-owned root anchors alone.");
		Assert.That(themeBody, Does.Not.Contain("anchorMin = new Vector2(0f,"),
			"Show-in-Ribbon theming must not stretch the dial root under HLG.");
		Assert.That(themeBody, Does.Contain("CircleRing"),
			"Dial theme must keep CircleRing (not SolidRect square).");
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
		Assert.That(src, Does.Contain("listBottomClearance"),
			"Nomad list padding must preserve bottom clearance so last rows are not Mask-clipped.");
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
