using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Canonical contract: Nomad / BoundChrome mutations must not stick on builtin default.
/// Run this filter whenever Nomad UI chrome changes: NomadThemeSiloContractTests
/// </summary>
public sealed class NomadThemeSiloContractTests {

	const string ThemeOpsPath =
		"Assets/_gm/Features/AddonSystem/SpzUiThemeOps.cs";

	/// <summary>Every public chrome mutator that must self-silo (source gate check).</summary>
	static readonly string[] GatedMutators = {
		"ApplyRoundedControlSprite",
		"FlattenToolFaceImage",
		"FlattenSlicedChromeFace",
		"ApplyControlLineIconAt",
		"ApplyNomadStackedToolCell",
		"ApplyBoundChromeTmp",
		"ApplyBoundChromeStripLabelTmp",
		"ApplyBoundChromeNarrowDockLabelTmp",
		"ApplyBoundChromePromptHeaderTmp",
		"ApplyBoundChromePromptPolaritySignTmp",
		"ApplyBoundChromeGraphic",
		"ApplyBoundChromeSelectable",
		"ApplySolidSquareChrome",
		"ApplyNomadSliderChrome",
		"HideAuthoredGraphicForTheme",
		"ApplyLineIconTint",
		"ApplyPanelWidth",
		"ApplyToAddonUiRoot",
		"ApplyContextMenuChrome",
	};

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ClearNomadUiFontCache();
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void BuiltinDefault_ShouldRecolorBoundChromeIsFalse() {
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.IsBuiltinDefaultActive, Is.True);
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
	}

	[Test]
	public void DangerousThemeOpsMethods_SourceContainsBoundChromeGate() {
		string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ThemeOpsPath));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);

		foreach (string method in GatedMutators) {
			int idx = src.IndexOf("public static void " + method, System.StringComparison.Ordinal);
			if (idx < 0)
				idx = src.IndexOf("static void " + method, System.StringComparison.Ordinal);
			Assert.That(idx, Is.GreaterThanOrEqualTo(0), "Missing method: " + method);
			int next = src.IndexOf("\n\t\tpublic static ", idx + 10, System.StringComparison.Ordinal);
			if (next < 0)
				next = src.IndexOf("\n\t\tstatic void ", idx + 10, System.StringComparison.Ordinal);
			if (next < 0)
				next = src.Length;
			string body = src.Substring(idx, next - idx);
			Assert.That(body, Does.Contain("ShouldRecolorBoundChrome"),
				method + " must gate on ShouldRecolorBoundChrome (Nomad silo contract)");
		}
	}

	[Test]
	public void StackedCell_BuiltinLeave_UnwindsFontAlignLayoutAndHidesIcon() {
		var root = new GameObject("SiloContractStack", typeof(RectTransform));
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			var faceGo = new GameObject("Face", typeof(RectTransform), typeof(Image));
			faceGo.transform.SetParent(root.transform, false);
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(faceGo.transform, false);
			var labelRt = labelGo.GetComponent<RectTransform>();
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = Vector2.zero;
			labelRt.offsetMax = Vector2.zero;
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "COLOR";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.alignment = TextAlignmentOptions.Top;
			var authoredFont = tmp.font;
			var authoredAlign = tmp.alignment;

			SpzUiThemeOps.ApplyNomadStackedToolCell(
				faceGo.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			Assert.That(tmp.alignment, Is.EqualTo(TextAlignmentOptions.Center));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyNomadStackedToolCell(
				faceGo.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			SpzUiThemeOps.RestoreBoundChromeUnder(faceGo.transform);

			Assert.That(tmp.font, Is.EqualTo(authoredFont));
			Assert.That(tmp.alignment, Is.EqualTo(authoredAlign));
			Assert.That(labelRt.anchorMax.y, Is.EqualTo(1f).Within(0.01f));
			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(faceGo.transform, "MonolithLineIcon");
			Assert.That(iconT == null || !iconT.gameObject.activeSelf, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void SolidSquareAndFlatten_NoOpOnBuiltin() {
		SpzUiThemeOps.ResetTheme();
		var go = new GameObject("SiloSolid", typeof(RectTransform), typeof(Image));
		var parent = new GameObject("Cell", typeof(RectTransform));
		go.transform.SetParent(parent.transform, false);
		try {
			var img = go.GetComponent<Image>();
			img.type = Image.Type.Sliced;
			var authoredSprite = img.sprite;
			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.2f, 0.2f);
			rt.anchorMax = new Vector2(0.8f, 0.8f);

			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			SpzUiThemeOps.FlattenToolFaceImage(img);

			Assert.That(img.sprite, Is.EqualTo(authoredSprite));
			Assert.That(UiRuntimeSprites.IsSolidRect(img.sprite), Is.False);
			Assert.That(rt.anchorMin.x, Is.EqualTo(0.2f).Within(0.001f));
			Assert.That(go.GetComponent<SpzUiThemeRoundedControl>(), Is.Null);
		}
		finally {
			Object.DestroyImmediate(parent);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ControlLineIcon_HiddenOnBuiltin() {
		var root = new GameObject("SiloIcon", typeof(RectTransform), typeof(Image));
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
				},
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyControlLineIcon(root.transform, StudioLineIcon.Trash, 16f);
			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(root.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			Assert.That(iconT.gameObject.activeSelf, Is.True);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyControlLineIcon(root.transform, StudioLineIcon.Trash, 16f);
			Assert.That(iconT.gameObject.activeSelf, Is.False);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void HideAuthoredGraphicAndLineIconTint_NoOpOnBuiltin() {
		SpzUiThemeOps.ResetTheme();
		var go = new GameObject("SiloHide", typeof(RectTransform), typeof(Image));
		try {
			var img = go.GetComponent<Image>();
			img.enabled = true;
			img.color = Color.white;
			SpzUiThemeOps.HideAuthoredGraphicForTheme(img);
			SpzUiThemeOps.ApplyLineIconTint(img);
			Assert.That(img.enabled, Is.True, "HideAuthoredGraphicForTheme must not hide on builtin");
			Assert.That(img.color, Is.EqualTo(Color.white), "ApplyLineIconTint must not retint on builtin");
			Assert.That(go.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Null);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyPanelWidth_NoOpOnBuiltin() {
		SpzUiThemeOps.ResetTheme();
		var go = new GameObject("SiloPanelW", typeof(RectTransform));
		try {
			var le = go.AddComponent<LayoutElement>();
			le.preferredWidth = 123f;
			le.minWidth = 100f;
			SpzUiThemeOps.ApplyPanelWidth(le);
			Assert.That(le.preferredWidth, Is.EqualTo(123f));
			Assert.That(le.minWidth, Is.EqualTo(100f));
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
