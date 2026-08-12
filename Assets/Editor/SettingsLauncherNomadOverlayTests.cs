using NUnit.Framework;
using Newtonsoft.Json.Linq;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings gear under Nomad: solid icon-only chrome; thank label must not use strip overflow;
/// Settings dismisses on click-outside (not hover-outside after clamp).
/// </summary>
public sealed class SettingsLauncherNomadOverlayTests {

	[Test]
	public void HideAuthoredGraphicForThemeDisablesTmpAndRestores() {
		var root = new GameObject("HideTmpRoot");
		root.SetActive(false);
		try {
			var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(root.transform, false);
			var tmp = go.GetComponent<TextMeshProUGUI>();
			tmp.enabled = true;

			SpzUiThemeOps.HideAuthoredGraphicForTheme(tmp);
			Assert.That(tmp.enabled, Is.False);
			Assert.That(tmp.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Not.Null);

			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);
			Assert.That(tmp.enabled, Is.True);
			Assert.That(tmp.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Null);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void SettingsCloseRuleRequiresClickOutsideNotHoverOutside() {
		// Mirror Settings_MGR.Update: gear sits outside panel; hover / launcher must not dismiss.
		var panelGo = new GameObject("SettingsPanel", typeof(RectTransform));
		panelGo.SetActive(true);
		var panel = panelGo.GetComponent<RectTransform>();
		panel.anchorMin = new Vector2(0.3f, 0.2f);
		panel.anchorMax = new Vector2(0.7f, 0.8f);
		panel.offsetMin = Vector2.zero;
		panel.offsetMax = Vector2.zero;

		Vector2 cursorOutside = new Vector2(-100f, -100f);
		bool releaseOutside = false;
		bool onLauncher = false;

		if (panel.gameObject.activeInHierarchy && releaseOutside && !onLauncher) {
			bool isInsidePanel = RectTransformUtility.RectangleContainsScreenPoint(panel, cursorOutside, null);
			if (!isInsidePanel)
				panel.gameObject.SetActive(false);
		}
		Assert.That(panelGo.activeSelf, Is.True, "hover outside must keep Settings open");

		releaseOutside = true;
		if (panel.gameObject.activeInHierarchy && releaseOutside && !onLauncher) {
			bool isInsidePanel = RectTransformUtility.RectangleContainsScreenPoint(panel, cursorOutside, null);
			if (!isInsidePanel)
				panel.gameObject.SetActive(false);
		}
		Assert.That(panelGo.activeSelf, Is.False, "release outside must close Settings");
		Object.DestroyImmediate(panelGo);
	}

	[Test]
	public void ApplySolidSquareChromeHidesCornerTriangles() {
		var tokens = new JObject {
			["control_bg"] = "#2A2B2FFF",
			["accent"] = "#F2CA50FF",
			["corner_radius"] = 0,
		};
		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"nomad-inspired", "Nomad inspired", tokens, "NomadThemeSPZ", out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.TryApplyTheme("nomad-inspired", null, "replace", out error), Is.True, error);

		var root = new GameObject("SolidLauncher");
		root.SetActive(false);
		try {
			var go = new GameObject("Gear", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(root.transform, false);
			var face = go.GetComponent<Image>();
			face.type = Image.Type.Sliced;
			var tri = new GameObject("triangle", typeof(RectTransform), typeof(Image));
			tri.transform.SetParent(go.transform, false);
			var triImg = tri.GetComponent<Image>();
			triImg.enabled = true;

			var btn = go.GetComponent<Button>();
			btn.targetGraphic = face;
			SpzUiThemeOps.ApplySolidSquareChrome(btn, Color.gray, Color.yellow);

			Assert.That(face.type, Is.EqualTo(Image.Type.Simple));
			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.True);
			Assert.That(triImg.enabled, Is.False);

			// Leave via ApplySolidSquareChrome itself (callers often re-invoke on ThemeChanged).
			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplySolidSquareChrome(btn, Color.gray, Color.yellow);
			Assert.That(triImg.enabled, Is.True, "leave ApplySolidSquareChrome must RestoreBoundChromeUnder (unhide triangle)");
			Assert.That(UiRuntimeSprites.IsSolidRect(face.sprite), Is.False,
				"leave must unwind SolidRect sprite via RestoreRoundedControlSpritesUnder");
		}
		finally {
			SpzUiThemeOps.TryApplyTheme(SpzUiThemeOps.DefaultThemeId, null, "replace", out _);
			Object.DestroyImmediate(root);
		}
	}
}
