using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BoundChrome stacked cells / strip labels must not leave TMP raycastTargets that swallow tool clicks.
/// </summary>
public sealed class BoundChromeLabelRaycastTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ClearNomadUiFontCache();
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void StackedToolCell_ClearsLabelRaycast_AndRestoresOnBuiltin() {
		var root = new GameObject("RaycastStack", typeof(RectTransform), typeof(Image), typeof(Toggle));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			var toggle = root.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(root.transform, false);
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "PAINT";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.raycastTarget = true;

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

			SpzUiThemeOps.ApplyNomadStackedToolCell(
				root.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			Assert.That(tmp.raycastTarget, Is.False);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyNomadStackedToolCell(
				root.transform, StudioLineIcon.Brush, SpzUiThemeOps.Active.textPrimary, 20f);
			Assert.That(tmp.raycastTarget, Is.True, "Restore SPZ must re-enable authored label raycasts");
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplySelectableToken_SetsDisabledColor() {
		var go = new GameObject("SelectableToken", typeof(RectTransform), typeof(Image), typeof(Button));
		go.SetActive(false);
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = go.GetComponent<Image>();
			var cb = btn.colors;
			cb.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.05f);
			btn.colors = cb;

			SpzUiThemeOps.ApplySelectableToken(btn, Color.gray, Color.yellow);
			Assert.That(btn.colors.disabledColor.a, Is.EqualTo(0.4f).Within(0.01f));
			Assert.That(btn.colors.colorMultiplier, Is.EqualTo(1f).Within(0.01f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ApplyToAddonUiRoot_DoesNotForcePanelWidthOnChildButtons() {
		var panel = new GameObject("AddonPanel_Test", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
		panel.SetActive(false);
		try {
			var panelLe = panel.GetComponent<LayoutElement>();
			panelLe.preferredWidth = 180f;

			var btnGo = new GameObject("ChildBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
			btnGo.transform.SetParent(panel.transform, false);
			var btn = btnGo.GetComponent<Button>();
			btn.targetGraphic = btnGo.GetComponent<Image>();
			var btnLe = btnGo.GetComponent<LayoutElement>();
			btnLe.preferredWidth = 48f;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["panel_width"] = 220f,
					["field_bg"] = "#22232AFF",
					["text_primary"] = "#E3E2E7FF",
					["text_muted"] = "#9A9BA3FF",
					["handle"] = "#F2CA50FF",
					["tab_active"] = "#343539FF",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyToAddonUiRoot(panel);
			Assert.That(panelLe.preferredWidth, Is.EqualTo(220f).Within(0.5f));
			Assert.That(btnLe.preferredWidth, Is.EqualTo(48f).Within(0.5f), "Child buttons must keep compact widths");
		}
		finally {
			Object.DestroyImmediate(panel);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void HideAuthoredGraphicForTheme_SkipsSelectableTargetGraphic() {
		var go = new GameObject("FaceIcon", typeof(RectTransform), typeof(Image), typeof(Button));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;
			face.enabled = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["accent"] = "#F2CA50FF" },
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.HideAuthoredGraphicForTheme(face);
			Assert.That(face.enabled, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
