using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SD resolution chips must highlight the matching W×H preset under BoundChrome.
/// </summary>
public sealed class SdResolutionPresetChromeSelectionTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void RefreshResolutionPresetChrome_SelectsMatchingSquarePreset() {
		var root = new GameObject("SdResPreset");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<SD_InputPanel_UI>();
			var width = root.AddComponent<IntegerInputField>();
			var height = root.AddComponent<IntegerInputField>();
			// IntegerInputField may need more wiring — use reflection on recentVal path via SetValue if available.
			SetPrivate(ui, "_width_input", EnsureIntField(root.transform, "W", 1024));
			SetPrivate(ui, "_height_input", EnsureIntField(root.transform, "H", 1024));
			SetPrivate(ui, "_resolutionPreset_512", MakePreset(root.transform, "512"));
			SetPrivate(ui, "_resolutionPreset_768", MakePreset(root.transform, "768"));
			SetPrivate(ui, "_resolutionPreset_1024", MakePreset(root.transform, "1024"));
			SetPrivate(ui, "_resolutionPreset_1536", MakePreset(root.transform, "1536"));
			SetPrivate(ui, "_resolutionPreset_2048", MakePreset(root.transform, "2048"));

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			ui.RefreshResolutionPresetChrome();
			var btn1024 = (Button)GetPrivate(ui, "_resolutionPreset_1024");
			var btn512 = (Button)GetPrivate(ui, "_resolutionPreset_512");
			Assert.That(ColorDistance(btn1024.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.35f)), Is.LessThan(0.05f));
			Assert.That(ColorDistance(btn512.targetGraphic.color, SpzUiThemeOps.Active.controlBg), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static IntegerInputField EnsureIntField(Transform parent, string name, int value) {
		var go = new GameObject(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		var field = go.AddComponent<IntegerInputField>();
		var recent = typeof(IntegerInputField).GetField(
			"_recentVal", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(recent, Is.Not.Null);
		recent.SetValue(field, value);
		return field;
	}

	static Button MakePreset(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
		go.transform.SetParent(parent, false);
		var btn = go.GetComponent<Button>();
		btn.targetGraphic = go.GetComponent<Image>();
		var label = new GameObject("Label", typeof(RectTransform));
		label.transform.SetParent(go.transform, false);
		label.AddComponent<TextMeshProUGUI>().text = name;
		return btn;
	}

	static void SetPrivate(object target, string field, object value) {
		var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, field);
		f.SetValue(target, value);
	}

	static object GetPrivate(object target, string field) {
		var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, field);
		return f.GetValue(target);
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
