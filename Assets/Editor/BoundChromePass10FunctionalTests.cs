using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 10: ThemeFlatToolToggle silo + PayMoney snapshot order (gen/options litmus).
/// </summary>
public sealed class BoundChromePass10FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeFlatToolToggle_EndsWithClearNonFaceRaycasts() {
		var go = new GameObject("SoftCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
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
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			var t = SpzUiThemeOps.Active;
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, t.controlBg, t.accent, t.textPrimary);

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
	public void PayMoney_SourceDoesNotClearRaycastBeforeTmpSnapshot() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main Viewport/PayMoney_button.cs"));
		string src = System.IO.File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("ClearNonFaceRaycastsForTheme"));
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp"));
		int poison = body.IndexOf("tmp.raycastTarget = false", System.StringComparison.Ordinal);
		int tmp = body.IndexOf("ApplyBoundChromeTmp", System.StringComparison.Ordinal);
		Assert.That(poison < 0 || poison > tmp, Is.True,
			"must not clear TMP raycast before BoundChrome snapshot");
	}

	[Test]
	public void Gen3DRembg_SourceUsesClearNonFace() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_rembg_button)"));
	}

	[Test]
	public void ApplyBoundChromeSelectable_ClearsNonFaceAndSkipsNestedSelectable() {
		var parent = new GameObject("ParentBtn", typeof(RectTransform), typeof(Image), typeof(Button));
		var child = new GameObject("ChildBtn", typeof(RectTransform), typeof(Image), typeof(Button));
		parent.SetActive(false);
		child.SetActive(false);
		try {
			child.transform.SetParent(parent.transform, false);
			var pFace = parent.GetComponent<Image>();
			pFace.raycastTarget = true;
			var pBtn = parent.GetComponent<Button>();
			pBtn.targetGraphic = pFace;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(parent.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			var cFace = child.GetComponent<Image>();
			cFace.raycastTarget = true;
			var cBtn = child.GetComponent<Button>();
			cBtn.targetGraphic = cFace;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			var t = SpzUiThemeOps.Active;
			SpzUiThemeOps.ApplyBoundChromeSelectable(pBtn, t.controlBg, t.accent);

			Assert.That(pFace.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False);
			Assert.That(cFace.raycastTarget, Is.True, "nested Selectable face must stay hittable");
		}
		finally {
			Object.DestroyImmediate(parent);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PinsZone_SourceClearsTmpRaycastAfterBoundChromeTmp() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Camera/Navigation/CamerasMGR_PinsZone_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyPinsChromeThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(2800, src.Length - idx));
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp"));
		Assert.That(body, Does.Contain("tmp.raycastTarget = false"));
		int tmp = body.IndexOf("ApplyBoundChromeTmp(tmp", System.StringComparison.Ordinal);
		int clear = body.IndexOf("tmp.raycastTarget = false", System.StringComparison.Ordinal);
		Assert.That(clear, Is.GreaterThan(tmp));
	}

	[Test]
	public void CircleSlider_SourceClearsValueTmpRaycastAfterBoundChrome() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/_Core/UI (reusable)/Widgets and Gadgets/Slider/CircleSlider_Snapping_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeTmp(_text"));
		Assert.That(src, Does.Contain("_text.raycastTarget = false"));
		int tmp = src.IndexOf("ApplyBoundChromeTmp(_text", System.StringComparison.Ordinal);
		int clear = src.IndexOf("_text.raycastTarget = false", System.StringComparison.Ordinal);
		Assert.That(clear, Is.GreaterThan(tmp));
	}

	[Test]
	public void MultiviewNumCams_SourceClearsNumberTextRaycast() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Camera/Multi-View/MultiView_Ribbon_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("_numCams_numberText.raycastTarget = false"));
	}

	[Test]
	public void MultiviewFov_SourceClearsNumberTextRaycastAfterBoundChromeTmp() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Camera/Multi-View/MultiView_CamerasFOV.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeTmp(_cam_FOV_numberText"));
		Assert.That(src, Does.Contain("_cam_FOV_numberText.raycastTarget = false"));
		int tmp = src.IndexOf("ApplyBoundChromeTmp(_cam_FOV_numberText", System.StringComparison.Ordinal);
		int clear = src.IndexOf("_cam_FOV_numberText.raycastTarget = false", System.StringComparison.Ordinal);
		Assert.That(clear, Is.GreaterThan(tmp));
	}

	[Test]
	public void StatusAndBrushDial_SourcesClearOverlayTmpRaycasts() {
		string status = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "_gm/Features/Viewport/Main Viewport/Viewport_StatusText.cs")));
		Assert.That(status, Does.Contain("_statusText.raycastTarget = false"));

		string opacity = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI_Opacity.cs")));
		Assert.That(opacity, Does.Contain("_brushOpacityText.raycastTarget = false"));

		string size = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI_Size.cs")));
		Assert.That(size, Does.Contain("_brushSize_text.raycastTarget = false"));
		Assert.That(size, Does.Contain("_brushSpacing_text.raycastTarget = false"));
	}
}
