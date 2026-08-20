using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>Native Nomad token body parity with Python NomadThemeSPZ TOKENS (rpc 1.18).</summary>
public sealed class NomadThemeComposeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		SpzUiThemeOps.TryUnregisterTheme("nomad-inspired", out _);
	}

	[Test]
	public void BuildNomadThemeTokensIncludesScalesMatchingPythonDefaults() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"BuildNomadThemeTokens",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null);
		var tokens = (JObject)method.Invoke(null, new object[] { 0.84f, 0.94f });
		Assert.That((string)tokens["accent"], Is.EqualTo("#F2CA50FF"));
		Assert.That((string)tokens["panel_bg"], Is.EqualTo("#1E1F23F2"));
		Assert.That((string)tokens["control_bg"], Is.EqualTo("#3E4048FF"));
		Assert.That((string)tokens["tab_active"], Is.EqualTo("#4A4C54FF"));
		Assert.That((string)tokens["border"], Is.EqualTo("#B8B0A099"));
		Assert.That((float)tokens["font_scale"], Is.EqualTo(0.84f).Within(0.001f));
		Assert.That((float)tokens["spacing_scale"], Is.EqualTo(0.94f).Within(0.001f));
		Assert.That((float)tokens["corner_radius"], Is.EqualTo(0f).Within(0.001f));
		Assert.That((string)tokens["icon_tint"], Is.EqualTo("#E8DFC8FF"));
		Assert.That((float)tokens["panel_width"], Is.EqualTo(220f).Within(0.001f));
		Assert.That((float)tokens["panel_alpha"], Is.EqualTo(0.92f).Within(0.001f));
		Assert.That((float)tokens["ribbon_icon_only"], Is.EqualTo(1f).Within(0.001f));
		AssertControlBgClearlyAbovePanelBg(tokens);

		Assert.That(SpzUiThemeOps.TryRegisterTheme(
			"nomad-inspired", "Nomad inspired", tokens, "NomadThemeSPZ", out string error), Is.True, error);
		Assert.That(SpzUiThemeOps.TryApplyTheme("nomad-inspired", null, "replace", out error), Is.True, error);
		Assert.That(SpzUiThemeOps.ActiveThemeId, Is.EqualTo("nomad-inspired"));
		Assert.That(SpzUiThemeOps.Active.fontScale, Is.EqualTo(0.84f).Within(0.001f));
		Assert.That(SpzUiThemeOps.Active.spacingScale, Is.EqualTo(0.94f).Within(0.001f));
		Assert.That(SpzUiThemeOps.Active.cornerRadius, Is.EqualTo(0f).Within(0.001f));
		Assert.That(SpzUiThemeOps.Active.panelWidth, Is.EqualTo(220f).Within(0.001f));
		Assert.That(SpzUiThemeOps.Active.panelAlpha, Is.EqualTo(0.92f).Within(0.001f));
		Assert.That(SpzUiThemeOps.RibbonIconOnlyActive, Is.True);
	}

	/// <summary>
	/// Left-column BoundChrome faces use control_bg on panel_bg; old #292A2E was only ~11 RGB above
	/// #1E1F23 and made bg/batch chips disappear. Keep a hard floor on mean RGB lift.
	/// </summary>
	[Test]
	public void NomadControlBgStaysClearlyAbovePanelBg() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"BuildNomadThemeTokens",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null);
		var tokens = (JObject)method.Invoke(null, new object[] { 0.84f, 0.94f });
		AssertControlBgClearlyAbovePanelBg(tokens);
	}

	static void AssertControlBgClearlyAbovePanelBg(JObject tokens) {
		Assert.That(ColorUtility.TryParseHtmlString((string)tokens["panel_bg"], out Color panel), Is.True);
		Assert.That(ColorUtility.TryParseHtmlString((string)tokens["control_bg"], out Color control), Is.True);
		float panelMean = (panel.r + panel.g + panel.b) / 3f;
		float controlMean = (control.r + control.g + control.b) / 3f;
		Assert.That(controlMean - panelMean, Is.GreaterThanOrEqualTo(0.10f),
			$"control_bg must lift ≥0.10 mean RGB above panel_bg (got {controlMean - panelMean:F3}); " +
			"left-panel buttons otherwise blend into charcoal.");
	}

	[Test]
	public void NomadStripIconMapParsesKnownStudioLineIcons() {
		string[] icons = { "Brush", "Eye", "Grid", "Mesh", "Settings" };
		foreach (string name in icons) {
			Assert.That(SpzUiThemeOps.TryParseStudioLineIcon(name, out StudioLineIcon icon, out string error),
				Is.True, error);
			Assert.That(icon.ToString(), Is.EqualTo(name));
		}
	}

	[Test]
	public void GetThemeComposesWithSkyboxAndKeepsUiScaleOnChrome() {
		var result = SpzUiThemeOps.GetThemeResult();
		Assert.That(result["composes_with"].ToString(), Does.Contain("spz.cmd.set_skybox_color"));
		Assert.That(result["composes_with"].ToString(), Does.Contain("spz.cmd.set_ui_scale"));
		Assert.That((string)result["ui_scale_source"], Is.EqualTo("chrome"));
	}
}
