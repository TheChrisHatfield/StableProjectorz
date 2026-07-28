using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prompt-header preset chips (and web-find) must be hard Nomad squares — not SPZ sliced round plates.
/// </summary>
public sealed class PromptPresetSquareChromeThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemePromptPresetSquareCell_UsesSolidRectSimpleAndEqualLayout() {
		var row = new GameObject("prompt header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
		row.SetActive(false);
		try {
			var hlg = row.GetComponent<HorizontalLayoutGroup>();
			hlg.spacing = 0f;

			var go = new GameObject("preset (toggle)", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
			go.transform.SetParent(row.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			face.pixelsPerUnitMultiplier = 1.7f;
			face.sprite = null; // authored atlas would be sliced; helper must replace
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var pressGo = new GameObject("pressed icon", typeof(RectTransform), typeof(Image));
			pressGo.transform.SetParent(go.transform, false);
			var press = pressGo.GetComponent<Image>();
			press.enabled = true;
			toggle.graphic = press;
			var le = go.GetComponent<LayoutElement>();
			le.preferredWidth = 30f;
			le.preferredHeight = -1f;
			le.minWidth = 25f;
			le.minHeight = -1f;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
					["corner_radius"] = 0,
					["spacing_scale"] = 0.94f,
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ThemePromptPresetSquareCell(toggle, SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent);

			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(le.preferredHeight, Is.EqualTo(le.preferredWidth).Within(0.01f));
			Assert.That(le.minHeight, Is.EqualTo(le.preferredWidth).Within(0.01f));
			Assert.That(press.enabled, Is.False);
			Assert.That(hlg.spacing, Is.GreaterThanOrEqualTo(3f));
		}
		finally {
			Object.DestroyImmediate(row);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void SdThemePromptPresetToggle_RoutesToSquareCell() {
		var go = new GameObject("preset (toggle)", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			go.GetComponent<LayoutElement>().preferredWidth = 30f;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
				},
				"replace",
				out string error), Is.True, error);

			var m = typeof(SD_InputPanel_UI).GetMethod(
				"ThemePromptPresetToggle",
				BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(m, Is.Not.Null);
			m.Invoke(null, new object[] { toggle, SpzUiThemeOps.Active });

			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
