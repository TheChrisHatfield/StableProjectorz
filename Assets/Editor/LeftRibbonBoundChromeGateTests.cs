using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Left ribbon selection sync must use BoundChrome gate (not hardcoded nomad-inspired id).
/// Depth Options slide-out is ownership-root themed (outside LeftRibbon transform).
/// </summary>
public sealed class LeftRibbonBoundChromeGateTests {

	[Test]
	public void SyncAndSnapshot_SourceUsesShouldRecolorBoundChrome() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main Viewport/LeftRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzUiThemeOps.ShouldRecolorBoundChrome"));
		Assert.That(src, Does.Contain("RestoreDepthOptionsMenuChrome"));
		Assert.That(src, Does.Contain("ThemeDepthOptionsMenu"));
		Assert.That(src, Does.Not.Contain("ActiveThemeId, \"nomad-inspired\""));
	}

	[Test]
	public void SnapshotNomadChromeSelection_TracksBoundChromeNotThemeId() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("LeftRibbonGate");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<LeftRibbon_UI>();
			var snap = typeof(LeftRibbon_UI).GetMethod(
				"SnapshotNomadChromeSelection", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(snap, Is.Not.Null);
			snap.Invoke(ui, null);
			var flag = typeof(LeftRibbon_UI).GetField(
				"_lastNomadChrome", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(flag, Is.Not.Null);
			Assert.That((bool)flag.GetValue(ui), Is.EqualTo(SpzUiThemeOps.ShouldRecolorBoundChrome));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyThemeTokens_Leave_RestoresDepthSlideOutOutsideRibbonHierarchy() {
		SpzUiThemeOps.ResetTheme();
		var ribbon = new GameObject("LeftRibbonGate", typeof(RectTransform));
		var menuHost = new GameObject("DepthMenuSibling", typeof(RectTransform));
		ribbon.SetActive(false);
		menuHost.SetActive(false);
		try {
			var slideGo = new GameObject("Depth Options panel (Slide widget)", typeof(RectTransform));
			slideGo.transform.SetParent(menuHost.transform, false);
			var slide = slideGo.AddComponent<SlideOut_Widget_UI>();

			var bgGo = new GameObject("Background", typeof(RectTransform));
			bgGo.transform.SetParent(slideGo.transform, false);
			var bg = bgGo.AddComponent<Image>();
			Color authored = new Color(0.3f, 0.25f, 0.2f, 1f);
			bg.color = authored;

			var headerGo = new GameObject("header (text)", typeof(RectTransform));
			headerGo.transform.SetParent(slideGo.transform, false);
			var header = headerGo.AddComponent<TextMeshProUGUI>();
			header.font = TMP_Settings.defaultFontAsset;
			header.color = Color.black;
			Color authoredHeader = header.color;

			var ui = ribbon.AddComponent<LeftRibbon_UI>();
			typeof(LeftRibbon_UI).GetField(
				"_depth_slideOut_panel", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(ui, slide);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["panel_bg"] = "#121317FF",
					["panel_alpha"] = 1.0,
					["text_primary"] = "#E3E2E7FF",
					["accent"] = "#F2CA50FF",
					["control_bg"] = "#292A2EFF",
				},
				"replace",
				out string error), Is.True, error);

			var apply = typeof(LeftRibbon_UI).GetMethod(
				"ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(apply, Is.Not.Null);
			apply.Invoke(ui, null);

			Assert.That(bg.color, Is.Not.EqualTo(authored), "BoundChrome should retint Depth menu Background");
			Assert.That(header.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));

			// Theme→theme: new tokens must refresh menu (not stick on first apply).
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment-b",
				new JObject {
					["panel_bg"] = "#203040FF",
					["panel_alpha"] = 1.0,
					["text_primary"] = "#AABBCCFF",
					["accent"] = "#112233FF",
					["control_bg"] = "#101820FF",
				},
				"replace",
				out error), Is.True, error);
			apply.Invoke(ui, null);
			Assert.That(header.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));

			SpzUiThemeOps.ResetTheme();
			apply.Invoke(ui, null);
			Assert.That(bg.color, Is.EqualTo(authored), "Leave must restore Depth menu outside ribbon root");
			Assert.That(header.color, Is.EqualTo(authoredHeader));
		}
		finally {
			Object.DestroyImmediate(ribbon);
			Object.DestroyImmediate(menuHost);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeToggle_DepInsideUsesCompactLabelNotStripTracking() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-dep-compact",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("DepCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var rt = go.GetComponent<RectTransform>();
			rt.sizeDelta = new Vector2(40f, 36f);
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.isOn = true;

			var labelGo = new GameObject("Input (text)", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var labelRt = labelGo.GetComponent<RectTransform>();
			labelRt.anchorMin = Vector2.zero;
			labelRt.anchorMax = Vector2.one;
			labelRt.offsetMin = Vector2.zero;
			labelRt.offsetMax = Vector2.zero;
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "inside";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.enableWordWrapping = true;

			var theme = typeof(LeftRibbon_UI).GetMethod(
				"ThemeToggle", BindingFlags.Static | BindingFlags.NonPublic);
			Assert.That(theme, Is.Not.Null);
			theme.Invoke(null, new object[] { toggle, SpzUiThemeOps.Active });

			Assert.That(tmp.enableWordWrapping, Is.False, "INSIDE must not wrap across DEP cell");
			Assert.That(tmp.characterSpacing, Is.LessThan(4f), "strip tracking 18 overflows LeftRibbon tool cells");
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ThemeToggle_SourceUsesCompactToolLabel() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main Viewport/LeftRibbon_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ThemeToggle", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("ApplyBoundChromeCompactToolLabelTmp"));
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeStripLabelTmp"));
	}
}
