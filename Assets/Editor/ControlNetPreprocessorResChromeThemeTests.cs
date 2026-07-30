using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ControlNet "res xN" chip is Image+hover (not Button) — must get BoundChrome or stays peach bevel.
/// </summary>
public sealed class ControlNetPreprocessorResChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokens_FlattensResChipFaceAndReverseOutLabel() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
				["corner_radius"] = 4,
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ResChip", typeof(RectTransform), typeof(Image), typeof(MouseHoverSensor_UI));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			face.color = new Color(0.98f, 0.91f, 0.87f, 1f);

			var labelGo = new GameObject("text", typeof(RectTransform));
			labelGo.transform.SetParent(root.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "res x2";
			label.color = Color.black;

			var opt = new GameObject("opt", typeof(RectTransform), typeof(Image), typeof(Toggle));
			opt.transform.SetParent(root.transform, false);
			var optFace = opt.GetComponent<Image>();
			optFace.type = Image.Type.Sliced;
			var optToggle = opt.GetComponent<Toggle>();
			optToggle.targetGraphic = optFace;

			var pre = root.AddComponent<ControlnetPreprocessor_UI>();
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_hoverMe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, root.GetComponent<MouseHoverSensor_UI>());
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_2", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, optToggle);

			pre.ApplyThemeTokens();

			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(label.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
			Assert.That(pre.OwnsResToggle(optToggle), Is.True);
			Assert.That(optFace.color.r, Is.LessThan(0.5f), "res radio must leave peach bevel");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ThemeResRadio_KeepsCompactLabelNotUndoneByBoundChromeTmp() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-res-compact",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ResCompact", typeof(RectTransform), typeof(Image), typeof(MouseHoverSensor_UI));
		root.SetActive(false);
		try {
			var slide = new GameObject("slide", typeof(RectTransform));
			slide.transform.SetParent(root.transform, false);

			var opt = new GameObject("opt", typeof(RectTransform), typeof(Image), typeof(Toggle));
			opt.transform.SetParent(slide.transform, false);
			var optFace = opt.GetComponent<Image>();
			var optToggle = opt.GetComponent<Toggle>();
			optToggle.targetGraphic = optFace;
			optToggle.isOn = true;

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(opt.transform, false);
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "1.5";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.enableWordWrapping = true;
			tmp.characterSpacing = 0f;

			var pre = root.AddComponent<ControlnetPreprocessor_UI>();
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_hoverMe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, root.GetComponent<MouseHoverSensor_UI>());
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_slideOut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, slide.transform);
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_15", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, optToggle);

			pre.ApplyThemeTokens();

			Assert.That(tmp.enableWordWrapping, Is.False, "CompactToolLabel must survive ThemeResRadio");
			Assert.That(tmp.characterSpacing, Is.LessThan(4f), "ApplyBoundChromeTmp after Flat must not restore ~10 tracking");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void BuiltinLeave_RestoresResChipFace() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["control_bg"] = "#292A2EFF", ["text_primary"] = "#E3E2E7FF" },
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ResLeave", typeof(RectTransform), typeof(Image), typeof(MouseHoverSensor_UI));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			Color peach = new Color(0.98f, 0.91f, 0.87f, 1f);
			face.color = peach;

			var pre = root.AddComponent<ControlnetPreprocessor_UI>();
			typeof(ControlnetPreprocessor_UI)
				.GetField("_preprocessorRes_hoverMe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pre, root.GetComponent<MouseHoverSensor_UI>());

			pre.ApplyThemeTokens();
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));

			SpzUiThemeOps.ResetTheme();
			pre.ApplyThemeTokens();
			Assert.That(face.color, Is.EqualTo(peach).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
