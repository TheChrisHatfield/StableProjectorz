using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass-2 BoundChrome selection sync: filters, resolution chips, eyedropper refresh, persist strip icons.
/// </summary>
public sealed class BoundChromeSelectionSyncPass2Tests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void SceneResolution_RefreshFilterToggleChrome_RetintsFromIsOn() {
		var root = new GameObject("SceneResFilter");
		root.SetActive(false);
		try {
			var mgr = root.AddComponent<SceneResolution_MGR>();
			var point = MakeToggle(root.transform, "Point");
			var bilin = MakeToggle(root.transform, "Bilinear");
			point.isOn = true;
			bilin.isOn = false;
			SetPrivate(mgr, "_textureFilterPoint_toggle", point);
			SetPrivate(mgr, "_textureFilterBilinear_toggle", bilin);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["success"] = "#3DCF8EFF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			mgr.RefreshFilterToggleChrome();
			Assert.That(ColorDistance(point.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.14f)), Is.LessThan(0.02f));

			point.isOn = false;
			bilin.isOn = true;
			mgr.RefreshFilterToggleChrome();
			Assert.That(ColorDistance(bilin.targetGraphic.color, Color.Lerp(
				SpzUiThemeOps.Active.controlBg, SpzUiThemeOps.Active.accent, 0.14f)), Is.LessThan(0.02f));
			Assert.That(ColorDistance(point.targetGraphic.color, SpzUiThemeOps.Active.controlBg), Is.LessThan(0.02f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void EyeDropperToggle_SourceCallsBrushRibbonApplyThemeTokens() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI_EyeDropperTool.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("BrushRibbon_UI.instance?.ApplyThemeTokens()"));
	}

	[Test]
	public void AddonUI_PersistRestore_SourceUsesShouldRecolorBoundChromeForStripIcons() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CoRestorePersistedThemeNextFrame"));
		Assert.That(src, Does.Contain("ShouldRecolorBoundChrome"));
		// Persist compose must not gate strip icons on hardcoded nomad-inspired alone.
		int composeIdx = src.IndexOf("ComposeNomadStripIconsNative()", System.StringComparison.Ordinal);
		Assert.That(composeIdx, Is.GreaterThan(0));
		string window = src.Substring(System.Math.Max(0, composeIdx - 220), 220);
		Assert.That(window, Does.Contain("ShouldRecolorBoundChrome"));
	}

	static Toggle MakeToggle(Transform parent, string name) {
		var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.transform.SetParent(parent, false);
		var face = go.GetComponent<Image>();
		var toggle = go.GetComponent<Toggle>();
		toggle.targetGraphic = face;
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
