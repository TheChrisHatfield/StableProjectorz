using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Exit / confirm popup Yes-No buttons must leave Unity default light gradient under Nomad.
/// </summary>
public sealed class ConfirmPopupChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokens_FlattensYesNoButtonsAndReverseOutLabels() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["panel_bg"] = "#1E1F23F2",
				["accent"] = "#F2CA50FF",
				["danger"] = "#E57373FF",
				["text_primary"] = "#E3E2E7FF",
				["corner_radius"] = 4,
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ConfirmPopup", typeof(RectTransform));
		root.SetActive(false);
		try {
			var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(Button));
			bgGo.transform.SetParent(root.transform, false);
			var bgImg = bgGo.GetComponent<Image>();
			bgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
			var bgBtn = bgGo.GetComponent<Button>();
			bgBtn.targetGraphic = bgImg;

			var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			panelGo.transform.SetParent(bgGo.transform, false);
			var panelImg = panelGo.GetComponent<Image>();
			panelImg.type = Image.Type.Sliced;
			panelImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);

			var headerGo = new GameObject("Header", typeof(RectTransform));
			headerGo.transform.SetParent(panelGo.transform, false);
			var header = headerGo.AddComponent<TextMeshProUGUI>();
			header.text = "Close the program?";

			Button MakeBtn(string name, Transform parent, out TextMeshProUGUI label) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(parent, false);
				var face = go.GetComponent<Image>();
				face.type = Image.Type.Sliced;
				face.color = new Color(0.85f, 0.85f, 0.85f, 1f); // Unity default light brick
				var btn = go.GetComponent<Button>();
				btn.targetGraphic = face;
				var labelGo = new GameObject("Text", typeof(RectTransform));
				labelGo.transform.SetParent(go.transform, false);
				label = labelGo.AddComponent<TextMeshProUGUI>();
				label.text = name;
				label.color = Color.black;
				label.raycastTarget = true;
				return btn;
			}

			var yes = MakeBtn("Close", panelGo.transform, out var yesText);
			var no = MakeBtn("DontClose", panelGo.transform, out var noText);

			var popup = root.AddComponent<ConfirmPopup_UI>();
			typeof(ConfirmPopup_UI).GetField("_background_button", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, bgBtn);
			typeof(ConfirmPopup_UI).GetField("_header", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, header);
			typeof(ConfirmPopup_UI).GetField("_yes", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, yes);
			typeof(ConfirmPopup_UI).GetField("_no", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, no);
			typeof(ConfirmPopup_UI).GetField("_yesText", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, yesText);
			typeof(ConfirmPopup_UI).GetField("_noText", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, noText);

			typeof(ConfirmPopup_UI).GetMethod("ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(popup, null);

			Assert.That(yes.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.danger));
			Assert.That(no.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(yesText.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
			Assert.That(noText.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
			Assert.That(yesText.raycastTarget, Is.False);
			Assert.That(noText.raycastTarget, Is.False);
			Assert.That(yes.targetGraphic.raycastTarget, Is.True);
			Assert.That(header.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void BuiltinLeave_RestoresConfirmPopupChrome() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["danger"] = "#E57373FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ConfirmLeave", typeof(RectTransform));
		root.SetActive(false);
		try {
			var bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(Button));
			bgGo.transform.SetParent(root.transform, false);
			var bgBtn = bgGo.GetComponent<Button>();
			bgBtn.targetGraphic = bgGo.GetComponent<Image>();

			var yesGo = new GameObject("Yes", typeof(RectTransform), typeof(Image), typeof(Button));
			yesGo.transform.SetParent(bgGo.transform, false);
			var yesFace = yesGo.GetComponent<Image>();
			Color light = new Color(0.85f, 0.85f, 0.85f, 1f);
			yesFace.color = light;
			var yes = yesGo.GetComponent<Button>();
			yes.targetGraphic = yesFace;

			var popup = root.AddComponent<ConfirmPopup_UI>();
			typeof(ConfirmPopup_UI).GetField("_background_button", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, bgBtn);
			typeof(ConfirmPopup_UI).GetField("_yes", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, yes);
			typeof(ConfirmPopup_UI).GetField("_no", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(popup, yes);

			typeof(ConfirmPopup_UI).GetMethod("ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(popup, null);
			Assert.That(yesFace.color, Is.EqualTo(SpzUiThemeOps.Active.danger));

			SpzUiThemeOps.ResetTheme();
			typeof(ConfirmPopup_UI).GetMethod("ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic)
				.Invoke(popup, null);
			Assert.That(yesFace.color, Is.EqualTo(light).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
