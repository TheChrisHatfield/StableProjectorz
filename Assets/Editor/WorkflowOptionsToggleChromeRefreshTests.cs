using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Soft Inpaint / Tileable / Ignore Depth: checkbox chrome must refresh on toggle, not only ThemeChanged.
/// </summary>
public sealed class WorkflowOptionsToggleChromeRefreshTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RefreshOptionToggleChrome_RetintsFromIsOn() {
		var root = new GameObject("WorkflowOptionsChrome");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<SD_WorkflowOptionsRibbon_UI>();
			var soft = MakeToggle(root.transform, "Soft");
			var tile = MakeToggle(root.transform, "Tile");
			var ignore = MakeToggle(root.transform, "Ignore");
			soft.isOn = true;
			tile.isOn = false;
			ignore.isOn = false;

			SetPrivate(ui, "_softInpaint", soft);
			SetPrivate(ui, "_tileableInpaint", tile);
			SetPrivate(ui, "_ignoreDepthOrNormals", ignore);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["tab_active"] = "#343539FF",
					["accent"] = "#F2CA50FF",
					["success"] = "#3DCF8EFF",
				},
				"replace",
				out string error), Is.True, error);

			ui.RefreshOptionToggleChrome();
			Color softOn = soft.targetGraphic.color;

			soft.isOn = false;
			tile.isOn = true;
			ui.RefreshOptionToggleChrome();

			Assert.That(ColorDistance(soft.targetGraphic.color, SpzUiThemeOps.Active.controlBg), Is.LessThan(0.02f));
			Assert.That(ColorDistance(tile.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f)), Is.LessThan(0.02f));
			Assert.That(ColorDistance(softOn, Color.Lerp(
				SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f)), Is.LessThan(0.02f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static Toggle MakeToggle(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.transform.SetParent(parent, false);
		var face = go.GetComponent<Image>();
		var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
		checkGo.transform.SetParent(go.transform, false);
		var toggle = go.GetComponent<Toggle>();
		toggle.targetGraphic = face;
		toggle.graphic = checkGo.GetComponent<Image>();
		return toggle;
	}

	static void SetPrivate(object target, string field, object value) {
		var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, field);
		f.SetValue(target, value);
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
