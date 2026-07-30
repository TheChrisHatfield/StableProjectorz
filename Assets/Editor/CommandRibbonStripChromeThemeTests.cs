using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Command ribbon strip menu items: Nomad flat fill (no gold 0.72 pill) + Restore SPZ unwind.
/// </summary>
public sealed class CommandRibbonStripChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeStripTabCellUsesFlatSelectedFillNotGoldPillThenRestoreUnwinds() {
		var root = new GameObject("CommandRibbonStripChromeTest");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<CommandRibbon_UI>();

			var strip = new GameObject("Strip", typeof(RectTransform));
			strip.transform.SetParent(root.transform, false);

			var cell = new GameObject("Tab: art list", typeof(RectTransform), typeof(Image), typeof(Button), typeof(TabsGroupElem_UI));
			cell.transform.SetParent(strip.transform, false);
			var face = cell.GetComponent<Image>();
			Color authoredFace = new Color(0.4f, 0.35f, 0.3f, 1f);
			face.color = authoredFace;
			var btn = cell.GetComponent<Button>();
			btn.targetGraphic = face;

			var goActive = new GameObject("go active", typeof(RectTransform));
			goActive.transform.SetParent(cell.transform, false);
			var pillGo = new GameObject("image", typeof(RectTransform), typeof(Image));
			pillGo.transform.SetParent(goActive.transform, false);
			var pill = pillGo.GetComponent<Image>();
			Color authoredPill = new Color(0.85f, 0.7f, 0.2f, 1f);
			pill.color = authoredPill;

			var elem = cell.GetComponent<TabsGroupElem_UI>();
			SetPrivate(elem, "_go_active", goActive);
			goActive.SetActive(true);

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(cell.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "ART";
			label.color = Color.white;

			// Point ribbon strip resolution at our strip via _tabGroup + first tab parent.
			var groupGo = new GameObject("TabsGroup", typeof(RectTransform), typeof(TabsGroup_UI));
			groupGo.transform.SetParent(root.transform, false);
			var group = groupGo.GetComponent<TabsGroup_UI>();
			var tabsField = typeof(TabsGroup_UI).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(tabsField, Is.Not.Null);
			tabsField.SetValue(group, new System.Collections.Generic.List<TabsGroupElem_UI> { elem });
			SetPrivate(ribbon, "_tabGroup", group);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["accent"] = "#F2CA50FF",
					["control_bg"] = "#25262AFF",
					["text_primary"] = "#E8E2D6FF",
					["ribbon_icon_only"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			var t = SpzUiThemeOps.Active;
			Color expectedSelected = Color.Lerp(t.controlBg, t.accent, 0.14f);
			Color goldPill = Color.Lerp(t.tabActive, t.accent, 0.72f);

			InvokeThemeStrip(ribbon, cell.transform, t, true, false);

			Assert.That(face.color, Is.EqualTo(expectedSelected));
			Assert.That(pill.color, Is.EqualTo(expectedSelected));
			Assert.That(ColorDistance(pill.color, goldPill), Is.GreaterThan(0.05f),
				"Selected pill must not use legacy gold 0.72 accent lerp");

			SpzUiThemeOps.ResetTheme();
			InvokeThemeStrip(ribbon, cell.transform, SpzUiThemeOps.Active, false, false);
			SpzUiThemeOps.RestoreBoundChromeUnder(cell.transform);

			Assert.That(face.color, Is.EqualTo(authoredFace));
			Assert.That(pill.color, Is.EqualTo(authoredPill));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}

	static void SetPrivate(object target, string fieldName, object value) {
		var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, fieldName);
		f.SetValue(target, value);
	}

	static void InvokeThemeStrip(object ribbon, Transform cell, SpzUiThemeOps.ThemeTokens t, bool recolor, bool iconOnly) {
		var m = typeof(CommandRibbon_UI).GetMethod("ThemeStripTabCell",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(m, Is.Not.Null);
		m.Invoke(ribbon, new object[] { cell, t, recolor, iconOnly });
	}
}
