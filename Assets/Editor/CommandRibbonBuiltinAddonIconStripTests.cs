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
			var iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(cell.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			Assert.That(iconT.gameObject.activeSelf, Is.True);
			var icon = iconT.GetComponent<Image>();
			Assert.That(icon, Is.Not.Null);
			Assert.That(icon.sprite, Is.Not.Null);
			Assert.That(icon.color.a, Is.GreaterThan(0.5f));

			var tip = face.GetComponent<CanShowTooltip_UI>();
			Assert.That(tip, Is.Not.Null, "hover name overlay must sit on the hit face");
			Assert.That(tip.tooltipText, Does.Contain("Demo").IgnoreCase);
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
