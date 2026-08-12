using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builtin CommandRibbon with enabled add-on tabs: SPZ-styled line icons + hover name (not Nomad BoundChrome).
/// </summary>
public sealed class CommandRibbonBuiltinAddonIconStripTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokens_Source_UsesBuiltinAddonIconStrip() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("StripHasEnabledAddonTabs"));
		Assert.That(src, Does.Contain("builtinAddonIconStrip"));
		Assert.That(src, Does.Contain("EnsureSpzDefaultStripLineIcon"));
		Assert.That(src, Does.Contain("SpzDefaultStripIconTint"));
		Assert.That(src, Does.Contain("Hide visible label glyphs in icon strip"));
		Assert.That(src, Does.Contain("Attach to the raycast face"));
		Assert.That(src, Does.Contain("Hidden labels must not steal hover/clicks"));
		Assert.That(src, Does.Contain("Harmonize-before-theme measured maxVisibleCharacters=0"));
		Assert.That(src, Does.Contain("Deactivate before Destroy so StripHasEnabledAddonTabs"));
		Assert.That(src, Does.Contain("ignore inactive / doomed GOs"));
		Assert.That(src, Does.Contain("re-apply strip icon/text chrome after repair"));
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
		// Prefab Art pattern: null targetGraphic, no TabBg — OG hits landed on TMP.
		// Builtin icon strip must not Ensure synthetic TabBg (sticky after Leave); keep TMP hits + tooltip on label.
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("BuiltinAddonIconPrefabTab");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();
			var cell = new GameObject("Tab: art list", typeof(RectTransform), typeof(Button), typeof(LayoutElement), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(root.transform, false);
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = null;
			cell.GetComponent<TabsGroupElem_UI>().InitForRuntime("art list", btn);

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "ART";
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
			Assert.That(label.raycastTarget, Is.True, "TMP-only prefab must keep label hits (no Ensure TabBg)");
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
