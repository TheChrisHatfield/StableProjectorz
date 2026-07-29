using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ControlNet thumb close must Ensure a hit face under Nomad (gen path: disable unit from thumbs strip).
/// </summary>
public sealed class ControlNetThumbCloseChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyThemeTokens_EnsuresCloseButtonHitFaceAndClearsLabelRaycast() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["danger"] = "#E57373FF",
				["text_primary"] = "#E3E2E7FF",
				["text_muted"] = "#9A9AA0FF",
			},
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("Thumb", typeof(RectTransform));
		root.SetActive(false);
		try {
			var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(Image));
			frameGo.transform.SetParent(root.transform, false);

			var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Button));
			closeGo.transform.SetParent(root.transform, false);
			var close = closeGo.GetComponent<Button>();
			close.targetGraphic = null; // prefab-style: clicked via TMP until Nomad clears label hits
			var labelGo = new GameObject("X", typeof(RectTransform));
			labelGo.transform.SetParent(closeGo.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "x";
			label.raycastTarget = true;

			var thumb = root.AddComponent<ControlNetUnit_Thumb_UI>();
			typeof(ControlNetUnit_Thumb_UI)
				.GetField("_frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(thumb, frameGo.GetComponent<Image>());
			typeof(ControlNetUnit_Thumb_UI)
				.GetField("_closeButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(thumb, close);

			typeof(ControlNetUnit_Thumb_UI)
				.GetMethod("ApplyThemeTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(thumb, null);

			Assert.That(close.targetGraphic, Is.Not.Null, "EnsureSelectableHitFace must wire a face");
			Assert.That(close.targetGraphic.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False, "label must not steal close clicks under Nomad");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void BuiltinLeave_RestoresThumbCloseChrome() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["control_bg"] = "#292A2EFF", ["danger"] = "#E57373FF", ["text_primary"] = "#EEE" },
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("ThumbLeave", typeof(RectTransform), typeof(Image), typeof(Button));
		root.SetActive(false);
		try {
			var face = root.GetComponent<Image>();
			face.color = new Color(0.93f, 0.87f, 0.83f, 0.5f);
			var btn = root.GetComponent<Button>();
			btn.targetGraphic = face;

			var thumb = root.AddComponent<ControlNetUnit_Thumb_UI>();
			typeof(ControlNetUnit_Thumb_UI)
				.GetField("_frame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(thumb, face);
			typeof(ControlNetUnit_Thumb_UI)
				.GetField("_closeButton", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(thumb, btn);

			typeof(ControlNetUnit_Thumb_UI)
				.GetMethod("ApplyThemeTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(thumb, null);
			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg).Within(0.001f));

			SpzUiThemeOps.ResetTheme();
			typeof(ControlNetUnit_Thumb_UI)
				.GetMethod("ApplyThemeTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(thumb, null);
			Assert.That(face.color.r, Is.EqualTo(0.93f).Within(0.02f));
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
