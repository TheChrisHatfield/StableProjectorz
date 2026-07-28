using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 9: secondary Graphics must not steal BoundChrome Selectable hits (POV/grid litmus).
/// </summary>
public sealed class BoundChromePass9FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ClearNonFaceRaycastsForTheme_KeepsFace_ClearsSecondary_RestoresOnLeave() {
		var go = new GameObject("PovCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.raycastTarget = true;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.raycastTarget = true;
			toggle.graphic = tick;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeSelectable(toggle, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);
			SpzUiThemeOps.HideAuthoredGraphicForTheme(tick);
			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(toggle);

			Assert.That(face.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False);
			Assert.That(tick.raycastTarget, Is.False);

			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);
			Assert.That(label.raycastTarget, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void BrushRibbonAndGenButtons_SourceUseClearNonFaceRaycasts() {
		string brush = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI.cs")));
		Assert.That(brush, Does.Contain("ClearNonFaceRaycastsForTheme(toggle)"));
		Assert.That(brush, Does.Contain("ClearNonFaceRaycastsForTheme(btn)"));

		string click = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/3D Clicking/ClickSelectMeshes_Toggle_UI.cs")));
		Assert.That(click, Does.Contain("ClearNonFaceRaycastsForTheme"));

		string gen = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Layouts/Viewport (MainView)/GenerateButtons_Main_UI.cs")));
		int themeGen = gen.IndexOf("static void ThemeGenButton", System.StringComparison.Ordinal);
		Assert.That(themeGen, Is.GreaterThan(0));
		string themeBody = gen.Substring(themeGen, System.Math.Min(1400, gen.Length - themeGen));
		Assert.That(themeBody, Does.Contain("ClearNonFaceRaycastsForTheme"));
		Assert.That(themeBody, Does.Contain("ApplyBoundChromeStripLabelTmp"));
		// Must not poison typography snapshot by clearing raycast before StripLabel.
		int poison = themeBody.IndexOf("label.raycastTarget = false", System.StringComparison.Ordinal);
		int strip = themeBody.IndexOf("ApplyBoundChromeStripLabelTmp", System.StringComparison.Ordinal);
		Assert.That(poison < 0 || poison > strip, Is.True);
	}

	[Test]
	public void MultiViewRefreshPov_SourceUsesClearNonFaceRaycasts() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Camera/Multi-View/MultiView_Ribbon_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		int idx = src.IndexOf("public void RefreshPovAndGridChromeSelection", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("ClearNonFaceRaycastsForTheme"));
	}
}
