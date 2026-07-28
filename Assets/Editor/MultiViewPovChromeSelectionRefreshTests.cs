using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Nomad Multiview: POV Checkmarks are hidden — selection fills must refresh on toggle, not only ThemeChanged.
/// </summary>
public sealed class MultiViewPovChromeSelectionRefreshTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RefreshPovChrome_AfterToggle_RetintsSelectedFill() {
		var root = new GameObject("MultiViewPovChrome");
		root.SetActive(false);
		try {
			var ribbon = root.AddComponent<MultiView_Ribbon_UI>();
			var pov0 = MakePovToggle(root.transform, "POV0");
			var pov1 = MakePovToggle(root.transform, "POV1");
			pov0.isOn = true;
			pov1.isOn = false;

			var listField = typeof(MultiView_Ribbon_UI).GetField(
				"_editPOV_toggles", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(listField, Is.Not.Null);
			listField.SetValue(ribbon, new List<Toggle> { pov0, pov1 });

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["tab_active"] = "#343539FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			ribbon.RefreshPovAndGridChromeSelection();
			Color selectedAtTheme = pov0.targetGraphic.color;

			pov0.isOn = false;
			pov1.isOn = true;
			ribbon.RefreshPovAndGridChromeSelection();

			Assert.That(pov1.targetGraphic.color, Is.EqualTo(Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.14f)));
			Assert.That(pov0.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(ColorDistance(pov1.targetGraphic.color, selectedAtTheme), Is.LessThan(0.01f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static Toggle MakePovToggle(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.transform.SetParent(parent, false);
		var face = go.GetComponent<Image>();
		var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
		tickGo.transform.SetParent(go.transform, false);
		var toggle = go.GetComponent<Toggle>();
		toggle.targetGraphic = face;
		toggle.graphic = tickGo.GetComponent<Image>();
		return toggle;
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
