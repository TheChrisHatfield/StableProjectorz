using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builtin CommandRibbon: only enabled add-on tabs borrow SPZ line icons; Art/Mesh/Paint stay OG text.
/// </summary>
public sealed class CommandRibbonBuiltinAddonIconStripTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokens_Source_ScopesIconsToAddonCellsOnly() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("StripHasEnabledAddonTabs"));
		Assert.That(src, Does.Contain("IsAddonStripTabCell"));
		Assert.That(src, Does.Contain("allowBuiltinAddonIcons"));
		Assert.That(src, Does.Contain("cellAddonIcon"));
		Assert.That(src, Does.Contain("EnsureSpzDefaultStripLineIcon"));
		Assert.That(src, Does.Contain("only enabled add-on tabs borrow"));
		Assert.That(src, Does.Contain("Harmonize-before-theme measured maxVisibleCharacters=0"));
		Assert.That(src, Does.Contain("Deactivate before Destroy so StripHasEnabledAddonTabs"));
	}

	[Test]
	public void IsAddonStripTabCell_DetectsAddonTitleNotPaint() {
		var paint = new GameObject("Tab: Paint", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
		var addon = new GameObject("Tab: Demo", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
		paint.SetActive(false);
		addon.SetActive(false);
		try {
			paint.GetComponent<TabsGroupElem_UI>().InitForRuntime("paint", paint.GetComponent<Button>());
			addon.GetComponent<TabsGroupElem_UI>().InitForRuntime("addon_Demo", addon.GetComponent<Button>());
			Assert.That(CommandRibbon_UI.IsAddonStripTabCell(paint.transform), Is.False);
			Assert.That(CommandRibbon_UI.IsAddonStripTabCell(addon.transform), Is.True);
		}
		finally {
			Object.DestroyImmediate(paint);
			Object.DestroyImmediate(addon);
		}
	}

	[Test]
	public void ThemeStripTabCell_BuiltinArt_KeepsTextEvenWhenAddonIconsAllowed() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("BuiltinArtWithAddons");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var cell = new GameObject("Tab: art list", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(root.transform, false);
			cell.GetComponent<TabsGroupElem_UI>().InitForRuntime("art list", cell.GetComponent<Button>());
			cell.GetComponent<Button>().targetGraphic = cell.GetComponent<Image>();

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "ART";
			label.maxVisibleCharacters = int.MaxValue;

			var iconGo = new GameObject("MonolithLineIcon", typeof(RectTransform), typeof(Image));
			iconGo.transform.SetParent(cell.transform, false);
			iconGo.SetActive(true);

			var theme = typeof(CommandRibbon_UI).GetMethod("ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			// hideStripLabels=false, builtinAddonIconStrip=false — Art is not an add-on cell.
			theme.Invoke(ribbon, new object[] {
				cell.transform,
				SpzUiThemeOps.Active,
				false,
				false,
				false,
			});

			Assert.That(label.maxVisibleCharacters, Is.EqualTo(int.MaxValue));
			Assert.That(iconGo.activeSelf, Is.False, "Art must not borrow Nomad/SPZ strip icons");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void StripHasEnabledAddonTabs_IgnoresInactiveAddonTabOnStrip() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("RibbonInactiveAddonScan");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var doomed = new GameObject("AddonTab_Gone", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
			doomed.transform.SetParent(root.transform, false);
			doomed.GetComponent<TabsGroupElem_UI>().InitForRuntime("addon_Gone", doomed.GetComponent<Button>());
			doomed.SetActive(false);

			var method = typeof(CommandRibbon_UI).GetMethod("StripHasEnabledAddonTabs", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(method, Is.Not.Null);
			var dictField = typeof(CommandRibbon_UI).GetField("_addonTabById", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(dictField, Is.Not.Null);
			var dict = dictField.GetValue(ribbon) as System.Collections.IDictionary;
			Assert.That(dict, Is.Not.Null);
			dict["Gone"] = doomed;

			Assert.That((bool)method.Invoke(ribbon, null), Is.False,
				"inactive/doomed addon tabs must not keep icon strip mode");

			doomed.SetActive(true);
			Assert.That((bool)method.Invoke(ribbon, null), Is.True,
				"active dict entry must still enable icon strip");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void RefreshRibbonTabStripLayout_Source_AppliesThemeBeforeHarmonize() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int refresh = src.IndexOf("void RefreshRibbonTabStripLayout", System.StringComparison.Ordinal);
		Assert.That(refresh, Is.GreaterThan(0));
		int next = src.IndexOf("void RefreshTabStripLayout()", refresh, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(refresh));
		string body = src.Substring(refresh, next - refresh);
		Assert.That(body, Does.Contain("ApplyThemeTokens()"));
		Assert.That(body, Does.Not.Contain("HarmonizeStripTabTypography()"),
			"Harmonize must run inside ApplyThemeTokens after labels are restored, not before");
	}

	[Test]
	public void ThemeStripTabCell_BuiltinAddonIcon_PrefabTmpOnly_KeepsLabelHitsAndTooltip() {
		// Addon tab with no TabBg — keep TMP hits; attach tooltip to label.
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("BuiltinAddonIconPrefabTab");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var cell = new GameObject("Tab: Demo", typeof(RectTransform), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(root.transform, false);
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null;
			cell.GetComponent<TabsGroupElem_UI>().InitForRuntime("addon_Demo", btn);

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "Demo Addon";
			label.raycastTarget = true;
			label.maxVisibleCharacters = int.MaxValue;

			var theme = typeof(CommandRibbon_UI).GetMethod("ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			theme.Invoke(ribbon, new object[] {
				cell.transform,
				SpzUiThemeOps.Active,
				false,
				true,
				true,
			});

			Assert.That(label.maxVisibleCharacters, Is.EqualTo(0));
			Assert.That(label.raycastTarget, Is.True, "TMP-only addon must keep label hits (no Ensure TabBg)");
			Assert.That(cell.transform.Find("TabBg"), Is.Null, "must not inject sticky synthetic TabBg");
			var tip = label.GetComponent<CanShowTooltip_UI>();
			Assert.That(tip, Is.Not.Null, "hover name must attach to the hittable label");
			var iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(cell.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			Assert.That(iconT.GetComponent<Image>().raycastTarget, Is.False,
				"line icon must never steal hits from TMP-only tabs");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ThemeStripTabCell_BuiltinWithAddonIcons_ShowsLineIconHidesLabelSetsTooltip() {
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);

		var root = new GameObject("BuiltinAddonIconRibbon");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var cell = new GameObject("AddonTab_Demo", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(root.transform, false);
			var elem = cell.GetComponent<TabsGroupElem_UI>();
			elem.InitForRuntime("addon_DemoAddon", cell.GetComponent<Button>());
			var face = cell.GetComponent<Image>();
			cell.GetComponent<Button>().targetGraphic = face;
			face.raycastTarget = true;

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "Demo Addon";
			label.maxVisibleCharacters = int.MaxValue;

			var theme = typeof(CommandRibbon_UI).GetMethod("ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(theme, Is.Not.Null);
			theme.Invoke(ribbon, new object[] {
				cell.transform,
				SpzUiThemeOps.Active,
				false,
				true,
				true,
			});

			Assert.That(label.maxVisibleCharacters, Is.EqualTo(0));
			Assert.That(label.raycastTarget, Is.False, "invisible labels must not steal hits from face/tooltip");
			Assert.That(face.raycastTarget, Is.True);
			var iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(cell.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			Assert.That(iconT.gameObject.activeSelf, Is.True);
			var icon = iconT.GetComponent<Image>();
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon.sprite, Is.Not.Null);
			Assert.That(icon.color.a, Is.GreaterThan(0.5f));
			Assert.That(icon.raycastTarget, Is.False);

			var tip = face.GetComponent<CanShowTooltip_UI>();
			Assert.That(tip, Is.Not.Null, "hover name overlay must sit on the hit face");
			Assert.That(tip.tooltipText, Does.Contain("Demo").IgnoreCase);
			var delayField = typeof(CanShowTooltip_UI).GetField("_hoverDelayBeforeShow",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(delayField, Is.Not.Null);
			Assert.That((float)delayField.GetValue(tip), Is.EqualTo(0.15f).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ResolveStripTabDisplayName_AddonPrefersHeaderLabelOverFolderId() {
		var cell = new GameObject("AddonTab_NomadThemeSPZ", typeof(RectTransform), typeof(Button), typeof(TabsGroupElem_UI));
		cell.SetActive(false);
		try {
			cell.GetComponent<TabsGroupElem_UI>().InitForRuntime("addon_NomadThemeSPZ", cell.GetComponent<Button>());
			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "Nomad Theme";
			Assert.That(CommandRibbon_UI.ResolveStripTabDisplayName(cell.transform), Is.EqualTo("Nomad Theme"));
		}
		finally {
			Object.DestroyImmediate(cell);
		}
	}

	[Test]
	public void TrySetStripTabLineIcon_BuiltinPaintStaysText_AddonKeepsIcon() {
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
		var root = new GameObject("SetLineIconRibbon");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var strip = new GameObject("Tabs", typeof(RectTransform));
			strip.transform.SetParent(root.transform, false);
			var tg = strip.AddComponent<TabsGroup_UI>();
			typeof(CommandRibbon_UI).GetField("_tabGroup", BindingFlags.Instance | BindingFlags.NonPublic)
				?.SetValue(ribbon, tg);

			var paint = new GameObject("Tab: Paint", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
			paint.transform.SetParent(strip.transform, false);
			paint.GetComponent<TabsGroupElem_UI>().InitForRuntime("paint", paint.GetComponent<Button>());
			paint.GetComponent<Button>().targetGraphic = paint.GetComponent<Image>();

			var addon = new GameObject("Tab: Demo", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TabsGroupElem_UI));
			addon.transform.SetParent(strip.transform, false);
			addon.GetComponent<TabsGroupElem_UI>().InitForRuntime("addon_Demo", addon.GetComponent<Button>());
			addon.GetComponent<Button>().targetGraphic = addon.GetComponent<Image>();
			var dict = typeof(CommandRibbon_UI).GetField("_addonTabById", BindingFlags.Instance | BindingFlags.NonPublic)
				?.GetValue(ribbon) as System.Collections.IDictionary;
			dict?.Add("Demo", addon);

			Assert.That(ribbon.TrySetStripTabLineIcon("Paint", StudioLineIcon.Brush, out string errPaint), Is.True, errPaint);
			var paintIcon = SpzUiThemeOps.FindDirectChildIncludingInactive(paint.transform, "MonolithLineIcon");
			Assert.That(paintIcon == null || !paintIcon.gameObject.activeSelf, Is.True,
				"Paint must stay text — set_line_icon must not force Monolith on builtin tabs");

			Assert.That(ribbon.TrySetStripTabLineIcon("Demo", StudioLineIcon.Settings, out string errAddon), Is.True, errAddon);
			var addonIcon = SpzUiThemeOps.FindDirectChildIncludingInactive(addon.transform, "MonolithLineIcon");
			Assert.That(addonIcon, Is.Not.Null);
			Assert.That(addonIcon.gameObject.activeSelf, Is.True, "add-on tabs borrow strip icons");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ThemeStripTabCell_BuiltinWithoutAddonIcons_HidesMonolith() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("BuiltinTextRibbon");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var cell = new GameObject("Tab: art list", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(root.transform, false);
			var iconGo = new GameObject("MonolithLineIcon", typeof(RectTransform), typeof(Image));
			iconGo.transform.SetParent(cell.transform, false);
			iconGo.SetActive(true);

			var theme = typeof(CommandRibbon_UI).GetMethod("ThemeStripTabCell",
				BindingFlags.Instance | BindingFlags.NonPublic);
			theme.Invoke(ribbon, new object[] {
				cell.transform,
				SpzUiThemeOps.Active,
				false,
				false,
				false,
			});

			Assert.That(iconGo.activeSelf, Is.False);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
