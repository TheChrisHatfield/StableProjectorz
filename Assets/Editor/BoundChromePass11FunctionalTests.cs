using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 11: DimensionMode BoundChrome must keep SD/3D/UV choice buttons hittable (gen path litmus).
/// </summary>
public sealed class BoundChromePass11FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void DimensionMode_SourceClearsNonFaceAfterFlatDiscs() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_3d_choice_button)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_sd_choice_button)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_uv_choice_button)"));
		Assert.That(src, Does.Contain("_mainChoice_text.raycastTarget = false"));
		int discs = src.IndexOf("ApplyFlatDiscsUnder(_bg_choice_button", System.StringComparison.Ordinal);
		int clear = src.IndexOf("ClearNonFaceRaycastsForTheme(_3d_choice_button)", System.StringComparison.Ordinal);
		Assert.That(clear, Is.GreaterThan(discs));
	}

	[Test]
	public void ClearNonFace_KeepsButtonFaceWhenCheckmarkOverlayPresent() {
		var go = new GameObject("SdChoice", typeof(RectTransform), typeof(Image), typeof(Button));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.raycastTarget = true;
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;
			var overlayGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			overlayGo.transform.SetParent(go.transform, false);
			var overlay = overlayGo.GetComponent<Image>();
			overlay.raycastTarget = true;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
			Assert.That(face.raycastTarget, Is.True);
			Assert.That(overlay.raycastTarget, Is.False);
			Assert.That(label.raycastTarget, Is.False);

			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);
			Assert.That(overlay.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void WorkflowOptionsThemeTmp_SourcesClearRaycastAfterBoundChrome() {
		string sd = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs")));
		int sdTheme = sd.IndexOf("static void ThemeTmp(", System.StringComparison.Ordinal);
		Assert.That(sdTheme, Is.GreaterThan(0));
		string sdBody = sd.Substring(sdTheme, System.Math.Min(350, sd.Length - sdTheme));
		Assert.That(sdBody, Does.Contain("ApplyBoundChromeTmp"));
		Assert.That(sdBody, Does.Contain("tmp.raycastTarget = false"));
		Assert.That(sdBody.IndexOf("tmp.raycastTarget = false", System.StringComparison.Ordinal),
			Is.GreaterThan(sdBody.IndexOf("ApplyBoundChromeTmp", System.StringComparison.Ordinal)));

		string g3 = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs")));
		int gTheme = g3.IndexOf("static void ThemeTmp(", System.StringComparison.Ordinal);
		Assert.That(gTheme, Is.GreaterThan(0));
		string gBody = g3.Substring(gTheme, System.Math.Min(350, g3.Length - gTheme));
		Assert.That(gBody, Does.Contain("tmp.raycastTarget = false"));
	}
}
