using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PROJ MASK workflow ribbon: Nomad sculpt stack (line icon above Roboto label).
/// </summary>
public sealed class WorkflowRibbonNomadStackThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ClearNomadUiFontCache();
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void ApplyNomadStackedToolCell_ThenBuiltinRestore_UnwindsFontAlignLayoutAndIcon() {
		var root = new GameObject("SiloStackCell", typeof(RectTransform));
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
			tmp.fontSize = 18f;
			tmp.alignment = TextAlignmentOptions.Top;
			var authoredFont = tmp.font;
			var authoredAlign = tmp.alignment;
			float authoredSize = tmp.fontSize;

			SpzUiThemeOps.ApplyNomadStackedToolCell(
				faceGo.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			Assert.That(labelRt.anchorMax.y, Is.LessThan(0.55f));
			Assert.That(labelRt.anchorMax.y, Is.GreaterThan(0.42f),
				"Workflow strip label band for 2-line caps");
			Assert.That(tmp.alignment, Is.EqualTo(TextAlignmentOptions.Center));
			var designTag = tmp.GetComponent<SpzUiThemeDesignFontPt>();
			Assert.That(designTag, Is.Not.Null);
			Assert.That(designTag.designPt, Is.GreaterThan(10f),
				"compact display size must not overwrite authored designPt (Restore SPZ blend litmus)");
			Assert.That(tmp.fontSize, Is.LessThan(12f), "Nomad stack uses compact display pt");

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyNomadStackedToolCell(
				faceGo.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			SpzUiThemeOps.RestoreBoundChromeUnder(faceGo.transform);

			Assert.That(tmp.font, Is.EqualTo(authoredFont), "Builtin must not keep Roboto");
			Assert.That(tmp.alignment, Is.EqualTo(authoredAlign), "Builtin must not keep Center stack align");
			Assert.That(labelRt.anchorMax.y, Is.EqualTo(1f).Within(0.01f), "Label rect must unwind");
			Assert.That(tmp.fontSize, Is.EqualTo(authoredSize).Within(0.05f),
				"authored point size must return after Leave Nomad");
			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(faceGo.transform, "MonolithLineIcon");
			Assert.That(iconT == null || !iconT.gameObject.activeSelf, Is.True, "Monolith icon hidden on builtin");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyRoundedControlSprite_NoOpsOnBuiltinDefault() {
		var go = new GameObject("SolidGate", typeof(RectTransform), typeof(Image));
		go.SetActive(false);
		try {
			SpzUiThemeOps.ResetTheme();
			Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);
			var img = go.GetComponent<Image>();
			img.type = Image.Type.Sliced;
			var authored = img.sprite;
			SpzUiThemeOps.ApplyRoundedControlSprite(img, markEligible: true);
			Assert.That(img.sprite, Is.EqualTo(authored));
			Assert.That(UiRuntimeSprites.IsSolidRect(img.sprite), Is.False);
			Assert.That(go.GetComponent<SpzUiThemeRoundedControl>(), Is.Null);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyNomadStackedToolCell_PlacesIconAboveLabelAndAppliesRoboto() {
		var root = new GameObject("WorkflowStackCell", typeof(RectTransform));
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["tab_active"] = "#E87A2CFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			var faceGo = new GameObject("Face", typeof(RectTransform), typeof(Image), typeof(Toggle));
			faceGo.transform.SetParent(root.transform, false);
			var faceRt = faceGo.GetComponent<RectTransform>();
			faceRt.sizeDelta = new Vector2(48, 72);
			var face = faceGo.GetComponent<Image>();
			var toggle = faceGo.GetComponent<Toggle>();
			toggle.targetGraphic = face;

			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(faceGo.transform, false);
			var labelRt = labelGo.GetComponent<RectTransform>();
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = Vector2.zero;
			labelRt.offsetMax = Vector2.zero;
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "NO COLOR";
			tmp.font = TMP_Settings.defaultFontAsset;

			SpzUiThemeOps.ApplyNomadStackedToolCell(
				faceGo.transform, StudioLineIcon.Drop, SpzUiThemeOps.Active.textPrimary, 14f);

			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(faceGo.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			var iconRt = iconT as RectTransform;
			Assert.That(iconRt, Is.Not.Null);
			Assert.That(iconRt.anchoredPosition.y, Is.GreaterThan(0f), "Icon sits above center");
			Assert.That(iconRt.anchoredPosition.y, Is.LessThan(6f),
				"Tight icon→label leading (was Grid 8px+ lift that left a sparse gap)");
			Assert.That(iconT.GetComponent<Image>().sprite,
				Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Drop)));

			Assert.That(labelRt.anchorMax.y, Is.GreaterThanOrEqualTo(0.42f),
				"Label band for PROJ MASK / NO COLOR second line");
			Assert.That(labelRt.anchorMax.y, Is.LessThanOrEqualTo(0.52f),
				"Label band stays below icon");
			Assert.That(tmp.characterSpacing, Is.LessThanOrEqualTo(3f),
				"Compact tracking so narrow cells do not wrap/overflow");
			Assert.That(tmp.lineSpacing, Is.LessThanOrEqualTo(-10f),
				"Tight leading so 2-line caps fit inside the cell");
			Assert.That(tmp.fontSize, Is.LessThanOrEqualTo(9f),
				"Reduced point size so labels stay inside rounded shell");
			Assert.That(tmp.overflowMode, Is.EqualTo(TextOverflowModes.Truncate),
				"Truncate — Overflow painted WHERE EMPTY past the box");
			var nomadFont = SpzUiThemeOps.ResolveNomadUiFont();
			Assert.That(tmp.font, Is.EqualTo(nomadFont));
			Assert.That((tmp.fontStyle & FontStyles.UpperCase) != 0, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeModeToggle_WiresCameraGlyphForProjMask() {
		var root = new GameObject("ProjMaskMode");
		root.SetActive(false);
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["tab_active"] = "#E87A2CFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["icon_tint"] = "#D0C5AFFF",
					["corner_radius"] = 0,
				},
				"replace",
				out string error), Is.True, error);

			var mode = root.AddComponent<WorkflowRibbon_ProjMask_UI>();
			var faceGo = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
			faceGo.transform.SetParent(root.transform, false);
			var toggle = faceGo.GetComponent<Toggle>();
			toggle.targetGraphic = faceGo.GetComponent<Image>();
			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(faceGo.transform, false);
			var checkImg = checkGo.GetComponent<Image>();
			toggle.graphic = checkImg;
			checkImg.enabled = true;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(faceGo.transform, false);
			labelGo.AddComponent<TextMeshProUGUI>().text = "PROJ MASK";

			var toggleField = typeof(WorkflowRibbon_ProjMask_UI).GetField(
				"_toggle", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(toggleField, Is.Not.Null);
			toggleField.SetValue(mode, toggle);

			var themeMode = typeof(WorkflowRibbon_UI).GetMethod(
				"ThemeModeToggle", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(themeMode, Is.Not.Null);
			themeMode.Invoke(null, new object[] { mode, true, StudioLineIcon.Camera, SpzUiThemeOps.Active });

			Transform iconT = SpzUiThemeOps.FindDirectChildIncludingInactive(faceGo.transform, "MonolithLineIcon");
			Assert.That(iconT, Is.Not.Null);
			Assert.That(iconT.GetComponent<Image>().sprite,
				Is.EqualTo(UiRuntimeSprites.GetLineIcon(StudioLineIcon.Camera)));
			Assert.That(checkImg.enabled, Is.False, "Workflow stacked cells hide Toggle Checkmark bevel under BoundChrome");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
