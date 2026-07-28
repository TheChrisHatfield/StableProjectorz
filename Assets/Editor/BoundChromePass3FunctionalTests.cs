using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 3 BoundChrome: Gen3D direction resolve, Gen3D options, Addon remember checkbox, Gen3D presets.
/// </summary>
public sealed class BoundChromePass3FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void ResolveDirection_SourcePrefersGen3dWhenDimGen3d() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("preferGen3d"));
		Assert.That(src, Does.Contain("ForEachDirectionHost"));
		Assert.That(src, Does.Contain("dim_gen_3d"));
	}

	[Test]
	public void Gen3dWorkflowOptions_RefreshOptionToggleChrome_RetintsFromIsOn() {
		var root = new GameObject("Gen3dOpts");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<Gen3D_WorkflowOptionsRibbon_UI>();
			var alpha = MakeToggle(root.transform, "Alpha");
			var shots = MakeToggle(root.transform, "Shots");
			alpha.isOn = true;
			shots.isOn = false;
			SetPrivate(ui, "_showAlphaOnly_toggle", alpha);
			SetPrivate(ui, "_makeScreenshots_toggle", shots);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["tab_active"] = "#343539FF",
					["accent"] = "#F2CA50FF",
					["success"] = "#3DCF8EFF",
				},
				"replace",
				out string error), Is.True, error);

			ui.RefreshOptionToggleChrome();
			Assert.That(ColorDistance(alpha.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f)), Is.LessThan(0.02f));

			alpha.isOn = false;
			shots.isOn = true;
			ui.RefreshOptionToggleChrome();
			Assert.That(ColorDistance(shots.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f)), Is.LessThan(0.02f));
			Assert.That(ColorDistance(alpha.targetGraphic.color, SpzUiThemeOps.Active.controlBg), Is.LessThan(0.02f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void AddonManagerRemember_SourceUsesThemeCheckboxToggle() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeCheckboxToggle(\n\t\t\t\t\t_rememberEnabledAddonToggle")
			.Or.Contain("ThemeCheckboxToggle(\r\n\t\t\t\t\t_rememberEnabledAddonToggle")
			.Or.Contain("ThemeCheckboxToggle(_rememberEnabledAddonToggle"));
		// Also accept multiline call from our edit.
		Assert.That(src.Contains("ThemeCheckboxToggle") && src.Contains("_rememberEnabledAddonToggle, t.controlBg"), Is.True);
	}

	[Test]
	public void Gen3dPrompt_RefreshPresetChrome_SelectsOnToggle() {
		var root = new GameObject("Gen3dPrompt");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<Generation3D_Prompt_UI>();
			var t0 = MakeToggle(root.transform, "P0");
			var t1 = MakeToggle(root.transform, "P1");
			t0.isOn = true;
			t1.isOn = false;
			var list = new System.Collections.Generic.List<Toggle> { t0, t1 };
			SetPrivate(ui, "_presetToggles", list);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			ui.RefreshPresetChrome();
			Assert.That(ColorDistance(t0.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.35f)), Is.LessThan(0.05f));

			t0.isOn = false;
			t1.isOn = true;
			ui.RefreshPresetChrome();
			Assert.That(ColorDistance(t1.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.35f)), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	static Toggle MakeToggle(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.transform.SetParent(parent, false);
		var face = go.GetComponent<Image>();
		var toggle = go.GetComponent<Toggle>();
		toggle.targetGraphic = face;
		var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
		checkGo.transform.SetParent(go.transform, false);
		toggle.graphic = checkGo.GetComponent<Image>();
		return toggle;
	}

	static void SetPrivate(object target, string field, object value) {
		var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, field);
		f.SetValue(target, value);
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
