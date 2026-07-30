using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gen3D preview pause OK/NO/Retry must leave Unity default light gradient under Nomad (gen litmus).
/// </summary>
public sealed class Gen3dVideoPreviewChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void Source_WiresThemeChangedAndBoundChromeOnDecisionButtons() {
		string path = "Assets/_gm/Features/3D Generate/Gen3D_InputPanelBuilder_UI/Gen3D_VideoPreview_UI.cs";
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeChanged += ApplyThemeTokens"));
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder"));
		Assert.That(src, Does.Contain("RestoreDecisionButton"));
		Assert.That(src, Does.Contain("OnConfirmed_GeneratePaused"));
	}

	[Test]
	public void ApplyThemeTokens_FlattensOkNoRetryAndKeepsHitFaces() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["success"] = "#81C784FF",
				["danger"] = "#E57373FF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
				["corner_radius"] = 4,
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("Gen3dVideoPreview", typeof(RectTransform));
		root.SetActive(false);
		try {
			var previewGo = new GameObject("VideoPreview", typeof(RectTransform));
			previewGo.transform.SetParent(root.transform, false);

			Button MakeBtn(string name, out TextMeshProUGUI label) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(previewGo.transform, false);
				var face = go.GetComponent<Image>();
				face.type = Image.Type.Sliced;
				face.color = new Color(0.85f, 0.85f, 0.85f, 1f);
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

			Toggle MakeTog(string name) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
				go.transform.SetParent(previewGo.transform, false);
				var face = go.GetComponent<Image>();
				face.color = new Color(0.7f, 0.7f, 0.7f, 1f);
				var tog = go.GetComponent<Toggle>();
				tog.targetGraphic = face;
				return tog;
			}

			var ok = MakeBtn("OK", out var okLabel);
			var no = MakeBtn("NO", out var noLabel);
			var retry = MakeBtn("Retry", out _);
			var gauss = MakeTog("Gauss");
			var mesh = MakeTog("Mesh");
			var rad = MakeTog("Radiance");

			var ui = root.AddComponent<Gen3D_VideoPreview_UI>();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(Gen3D_VideoPreview_UI).GetField("_videoPreview_go", flags).SetValue(ui, previewGo);
			typeof(Gen3D_VideoPreview_UI).GetField("_decisionButtons_go", flags).SetValue(ui, previewGo);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_OK_button", flags).SetValue(ui, ok);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_NO_button", flags).SetValue(ui, no);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_retry_button", flags).SetValue(ui, retry);
			typeof(Gen3D_VideoPreview_UI).GetField("_gauss_toggle", flags).SetValue(ui, gauss);
			typeof(Gen3D_VideoPreview_UI).GetField("_mesh_toggle", flags).SetValue(ui, mesh);
			typeof(Gen3D_VideoPreview_UI).GetField("_radiance_toggle", flags).SetValue(ui, rad);

			typeof(Gen3D_VideoPreview_UI).GetMethod("ApplyThemeTokens", flags).Invoke(ui, null);

			Assert.That(ok.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.success));
			Assert.That(no.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.danger));
			Assert.That(retry.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(ok.targetGraphic.raycastTarget, Is.True);
			Assert.That(okLabel.raycastTarget, Is.False);
			Assert.That(noLabel.raycastTarget, Is.False);
			Assert.That(gauss.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void BuiltinLeave_RestoresDecisionChrome() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["success"] = "#81C784FF",
				["danger"] = "#E57373FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("Gen3dLeave", typeof(RectTransform));
		root.SetActive(false);
		try {
			var previewGo = new GameObject("VideoPreview", typeof(RectTransform));
			previewGo.transform.SetParent(root.transform, false);

			Button MakeBtn(string name, Color authored) {
				var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(previewGo.transform, false);
				var face = go.GetComponent<Image>();
				face.color = authored;
				var btn = go.GetComponent<Button>();
				btn.targetGraphic = face;
				return btn;
			}

			Color light = new Color(0.85f, 0.85f, 0.85f, 1f);
			var ok = MakeBtn("OK", light);
			var no = MakeBtn("NO", light);
			var retry = MakeBtn("Retry", light);

			var ui = root.AddComponent<Gen3D_VideoPreview_UI>();
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			typeof(Gen3D_VideoPreview_UI).GetField("_videoPreview_go", flags).SetValue(ui, previewGo);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_OK_button", flags).SetValue(ui, ok);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_NO_button", flags).SetValue(ui, no);
			typeof(Gen3D_VideoPreview_UI).GetField("_video_retry_button", flags).SetValue(ui, retry);

			typeof(Gen3D_VideoPreview_UI).GetMethod("ApplyThemeTokens", flags).Invoke(ui, null);
			Assert.That(ok.targetGraphic.color, Is.EqualTo(SpzUiThemeOps.Active.success));

			SpzUiThemeOps.ResetTheme();
			typeof(Gen3D_VideoPreview_UI).GetMethod("ApplyThemeTokens", flags).Invoke(ui, null);
			Assert.That(ok.targetGraphic.color.r, Is.EqualTo(light.r).Within(0.001f));
			Assert.That(ok.targetGraphic.color.g, Is.EqualTo(light.g).Within(0.001f));
			Assert.That(ok.targetGraphic.color.b, Is.EqualTo(light.b).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
