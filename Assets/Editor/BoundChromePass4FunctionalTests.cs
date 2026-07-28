using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 4 BoundChrome: addon toggle refresh, PaintToolFaceColor, context-menu BoundChrome helpers.
/// </summary>
public sealed class BoundChromePass4FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ApplyToAddonUiRoot_RetintsToggleFromIsOn_ViaThemeCheckboxToggle() {
		var root = new GameObject("AddonPanel_Pass4", typeof(RectTransform), typeof(Image));
		root.SetActive(false);
		try {
			var togGo = new GameObject("Tog", typeof(RectTransform), typeof(Image), typeof(Toggle));
			togGo.transform.SetParent(root.transform, false);
			var face = togGo.GetComponent<Image>();
			var toggle = togGo.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(togGo.transform, false);
			toggle.graphic = checkGo.GetComponent<Image>();
			toggle.isOn = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["tab_active"] = "#343539FF",
					["accent"] = "#F2CA50FF",
					["success"] = "#3DCF8EFF",
					["field_bg"] = "#22232AFF",
					["text_primary"] = "#E3E2E7FF",
					["text_muted"] = "#9A9BA3FF",
					["handle"] = "#F2CA50FF",
					["panel_bg"] = "#1E1F23F2",
				},
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyToAddonUiRoot(root);
			Assert.That(ColorDistance(face.color, Color.Lerp(
				SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f)), Is.LessThan(0.05f));
			Assert.That(toggle.graphic.enabled, Is.True);

			toggle.isOn = false;
			SpzUiThemeOps.ApplyToAddonUiRoot(root);
			Assert.That(ColorDistance(face.color, SpzUiThemeOps.Active.controlBg), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PaintTab_SourceUsesPaintToolFaceColorHelper() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Paint/PaintTab/PaintTab_CollectPaintUI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PaintToolFaceColor"));
		Assert.That(src, Does.Contain("MakeDepthLimitToggle"));
		Assert.That(src, Does.Contain("MakePaintSymmetryToggle"));
		Assert.That(src, Does.Contain("SyncFlipToggleFromStore"));
	}

	[Test]
	public void ApplyContextMenuChrome_SourceUsesApplyBoundChromeSelectable() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void ApplyContextMenuChrome", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		int next = src.IndexOf("\n\t\tpublic static ", idx + 20, System.StringComparison.Ordinal);
		if (next < 0) next = src.Length;
		string body = src.Substring(idx, next - idx);
		Assert.That(body, Does.Contain("ApplyBoundChromeSelectable"));
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp"));
	}

	[Test]
	public void ViewportContextMenu_SourceWiresThemeChanged() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main View ContextMenu/ViewportContextMenu_Art_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzUiThemeOps.ThemeChanged += ApplyThemeTokens"));
		Assert.That(src, Does.Contain("ApplyContextMenuChrome"));
	}

	[Test]
	public void AddonAddToggle_SourceAssignsGraphicBeforeRoundedSprite() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int graphic = src.IndexOf("toggle.graphic = checkImg;", System.StringComparison.Ordinal);
		int rounded = src.IndexOf("ApplyRoundedControlSprite(checkImg", System.StringComparison.Ordinal);
		Assert.That(graphic, Is.GreaterThan(0));
		Assert.That(rounded, Is.GreaterThan(graphic));
		Assert.That(src, Does.Contain("ApplyToAddonUiRoot(toggleObj)"));
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
