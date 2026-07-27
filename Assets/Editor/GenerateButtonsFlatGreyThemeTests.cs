using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Under Nomad, GEN ART / FULL dock use flat grey fills + visible text (not beveled peach / icon-only).</summary>
public sealed class GenerateButtonsFlatGreyThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeGenButtonUsesFlatControlBgNotBeveledPeach() {
		var root = new GameObject("GenArtFlatGreyTest");
		root.SetActive(false);
		try {
			var btnGo = new GameObject("GenArt", typeof(RectTransform), typeof(Image), typeof(Button));
			btnGo.transform.SetParent(root.transform, false);
			var face = btnGo.GetComponent<Image>();
			Color peach = new Color(0.85f, 0.56f, 0.35f, 1f);
			face.color = peach;
			var btn = btnGo.GetComponent<Button>();
			btn.targetGraphic = face;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(btnGo.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.text = "GEN\nART";

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["accent"] = "#F2CA50FF", ["text_primary"] = "#E3E2E7FF" },
				"replace",
				out string error), Is.True, error);

			var themeGen = typeof(GenerateButtons_UI).GetMethod(
				"ThemeGenButton",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			Assert.That(themeGen, Is.Not.Null);
			themeGen.Invoke(null, new object[] { btn, SpzUiThemeOps.Active });

			Assert.That(face.color, Is.EqualTo(SpzUiThemeOps.Active.controlBg));
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(ColorDistance(face.color, peach), Is.GreaterThan(0.2f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyGenButtonFaceUnderNomadPreservesRgbOnlyAdjustsAlpha() {
		var root = new GameObject("GenArtHoverPreserve");
		root.SetActive(false);
		try {
			var btnGo = new GameObject("GenArt", typeof(RectTransform), typeof(Image), typeof(Button));
			btnGo.transform.SetParent(root.transform, false);
			var face = btnGo.GetComponent<Image>();
			var btn = btnGo.GetComponent<Button>();
			btn.targetGraphic = face;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["accent"] = "#F2CA50FF" },
				"replace",
				out string error), Is.True, error);

			var themeGen = typeof(GenerateButtons_UI).GetMethod(
				"ThemeGenButton",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			themeGen.Invoke(null, new object[] { btn, SpzUiThemeOps.Active });

			Color afterTheme = face.color;
			face.color = new Color(afterTheme.r, afterTheme.g, afterTheme.b, 1f);
			// Simulate ColorTint hover multiply leftover on RGB — ApplyGenButtonFace must not reset to controlBg.
			face.color = new Color(0.95f, 0.90f, 0.70f, 1f);

			var applyFace = typeof(GenerateButtons_UI).GetMethod(
				"ApplyGenButtonFace",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			Assert.That(applyFace, Is.Not.Null);
			applyFace.Invoke(null, new object[] { btn, false });

			Assert.That(face.color.r, Is.EqualTo(0.95f).Within(0.001f));
			Assert.That(face.color.g, Is.EqualTo(0.90f).Within(0.001f));
			Assert.That(face.color.b, Is.EqualTo(0.70f).Within(0.001f));
			Assert.That(face.color.a, Is.EqualTo(0.5f).Within(0.001f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
