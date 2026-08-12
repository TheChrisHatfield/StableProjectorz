using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 7: flat tool radios (Soft/Tileable/Point), FULL SRN no black snapshot, AddonUI checkmark.
/// </summary>
public sealed class BoundChromePass7FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeFlatToolToggle_HidesBevelCheckmarkAndClearsRaycast() {
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

		var go = new GameObject("SoftCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.isOn = true;
			var ckGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			ckGo.transform.SetParent(go.transform, false);
			var ck = ckGo.GetComponent<Image>();
			ck.raycastTarget = true;
			toggle.graphic = ck;

			Color faceCol = Color.Lerp(SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f);
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, faceCol, SpzUiThemeOps.Active.accent, SpzUiThemeOps.Active.textPrimary);

			Assert.That(ck.raycastTarget, Is.False);
			Assert.That(ck.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Not.Null);
			Assert.That(ColorDistance(face.color, faceCol), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ThemeFlatToolToggle_SoftLabelFitsWithoutStripTrackingWrap() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-soft-label",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["tab_active"] = "#343539FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("SoftLabelCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var rt = go.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(44f, 36f);
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.isOn = true;

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var labelRt = labelGo.GetComponent<RectTransform>();
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = Vector2.zero;
			labelRt.offsetMax = Vector2.zero;
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "soft";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.fontSize = 18f;
			tmp.enableWordWrapping = true;

			Color faceCol = Color.Lerp(SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f);
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, faceCol, SpzUiThemeOps.Active.accent, SpzUiThemeOps.Active.textPrimary);

			Assert.That(tmp.enableWordWrapping, Is.False, "SOFT must not wrap to SOF/T across Soft|Tile boundary");
			Assert.That(tmp.characterSpacing, Is.LessThan(4f), "strip tracking (18) overflows Soft cell");
			Assert.That(tmp.overflowMode, Is.EqualTo(TextOverflowModes.Truncate));
			Assert.That((tmp.fontStyle & FontStyles.UpperCase) != 0, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void SoftTileable_SourceUsesThemeFlatToolToggle() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeFlatToolToggle(toggle"));
		Assert.That(src, Does.Not.Contain("ThemeCheckboxToggle(toggle, face, tokens.accent, tokens.success)"));
	}

	[Test]
	public void Gen3DAndPointBilinear_SourceUseThemeFlatToolToggle() {
		string gen3d = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs")));
		string res = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/Settings/SceneResolution_MGR.cs")));
		Assert.That(gen3d, Does.Contain("ThemeFlatToolToggle(toggle"));
		Assert.That(res, Does.Contain("ThemeFlatToolToggle(tgl"));
	}

	[Test]
	public void FullSrnLabelStyle_SourceDoesNotHardcodeColorBlack() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/RibbonViewportFullViewOnScreen_Toggle_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFullSrnLabelStyle", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(2200, src.Length - idx));
		Assert.That(body, Does.Not.Contain("tmp.color = Color.black"));
		Assert.That(body, Does.Contain("ShouldRecolorBoundChrome"));
		Assert.That(body, Does.Contain("ApplyBoundChromeNarrowDockLabelTmp"));
		Assert.That(body, Does.Contain("EnsureDesignFontPt"));
		Assert.That(body, Does.Not.Contain("tmp.fontSize = 18f"));
		Assert.That(body, Does.Not.Contain("Mathf.Max(12f, genRefTmp.fontSize)"));
	}

	[Test]
	public void FullSrnDock_SourceUsesAdaptiveDimModeClearanceAndLargerLabel() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/RibbonViewportFullViewOnScreen_Toggle_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("const float DockLabelBasePt = 13f"));
		Assert.That(src, Does.Contain("ApplyAdaptiveBottomGap"));
		Assert.That(src, Does.Contain("MeasureDimModeOverlapDeficitLocalPx"));
		Assert.That(src, Does.Contain("MainChoiceVisualRect"));
		Assert.That(src, Does.Contain("MinAdaptiveBottomGapPx"));
		Assert.That(src, Does.Contain("SuppressGenerateButtonsColumnFrame"));
		Assert.That(src, Does.Contain("EnsureAdaptiveFaceBorder"));
		Assert.That(src, Does.Contain("DockFaceBorder"));
		Assert.That(src, Does.Contain("FindDirectChildIncludingInactive(face, FaceBorderName)"));
		int onDestroy = src.IndexOf("void OnDestroy()", System.StringComparison.Ordinal);
		Assert.That(onDestroy, Is.GreaterThan(0));
		string destroyBody = src.Substring(onDestroy, System.Math.Min(500, src.Length - onDestroy));
		Assert.That(destroyBody, Does.Contain("TearDownBuiltDock()"),
			"OnDestroy must TearDown so GenerateButtons column frame is restored");
		Assert.That(src, Does.Contain("FindDirectChildIncludingInactive(parent, \"LineIcon\")"),
			"LineIcon ensure must not use Transform.Find (inactive OPEN RIGHT duplicates)");
		Assert.That(src, Does.Contain("float labelPt = forceFullSrnLabel ? DockLabelBasePt : (DockLabelBasePt - 1f)"));
		Assert.That(src, Does.Contain("const float openRightLabelPt = DockLabelBasePt - 1f"));
		Assert.That(src, Does.Contain("s_columnFrameSuppressCount"));
		Assert.That(src, Does.Contain("FindDirectChildIncludingInactive(vlgRoot, MenuRowName)"));
		Assert.That(src, Does.Contain("ResetColumnFrameSuppressStatics"));
		Assert.That(src, Does.Contain("RuntimeInitializeLoadType.SubsystemRegistration"));
		Assert.That(src, Does.Contain("RegisteredInstances.Clear()"));
		Assert.That(src, Does.Contain("PendingDockSpecs.Clear()"));
		Assert.That(src, Does.Contain("TryGetOpenChoicesPanelVisualRect"));
		Assert.That(src, Does.Contain("choicesBottomLocal"));
		Assert.That(src, Does.Contain("_lastDimChoicesFanOpen"));
	}

	[Test]
	public void NarrowDockLabel_SeedsDesignPtAndEasesTracking() {
		var go = new GameObject("FullSrnNarrowDockLabel");
		var tmp = go.AddComponent<TextMeshProUGUI>();
		// Unity TMP default — previously poisoned BoundChrome design capture (~36).
		tmp.fontSize = 36f;
		tmp.characterSpacing = 0f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["tab_active"] = "#343539FF",
					["text_primary"] = "#E3E2E7FF",
					["font_scale"] = 1f,
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(tmp, SpzUiThemeOps.Active.textPrimary, 11f);
			Assert.That(tmp.fontSize, Is.EqualTo(11f).Within(0.05f),
				"Must not keep TMP default 36 as scaled design size");
			Assert.That(tmp.characterSpacing, Is.LessThan(12f),
				"Narrow dock tracking must be milder than full strip 18");
			var tag = tmp.GetComponent<SpzUiThemeDesignFontPt>();
			Assert.That(tag, Is.Not.Null);
			Assert.That(tag.designPt, Is.EqualTo(11f).Within(0.05f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeNarrowDockLabelTmp(tmp, Color.white, 11f);
			Assert.That(tmp.fontSize, Is.EqualTo(11f).Within(0.05f));
		} finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void AddonAddToggle_SourceDoesNotRoundCheckmark() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("toggle.graphic = checkImg;"));
		Assert.That(src, Does.Not.Contain("ApplyRoundedControlSprite(checkImg"));
	}

	[Test]
	public void DimensionMode_SourceThemesNamedCheckmarkFaces() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFlatDiscsUnder", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(800, src.Length - idx));
		Assert.That(body, Does.Contain("IsToggleCheckmarkGraphic"));
		Assert.That(body, Does.Not.Contain("n.Equals(\"Checkmark\""));
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
