using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 7: flat tool radios (Soft/Tileable/Point), FULL SRN no black snapshot, AddonUI checkmark.
/// </summary>
public sealed class BoundChromePass7FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeFlatToolToggle_HidesBevelCheckmarkAndClearsRaycast() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["tab_active"] = "#343539FF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("SoftCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			toggle.isOn = true;
			var ckGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			ckGo.transform.SetParent(go.transform, false);
			var ck = ckGo.GetComponent<Image>();
			ck.raycastTarget = true;
			toggle.graphic = ck;

			Color faceCol = Color.Lerp(SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f);
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, faceCol, SpzUiThemeOps.Active.accent, SpzUiThemeOps.Active.textPrimary);

			Assert.That(ck.raycastTarget, Is.False);
			Assert.That(ck.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Not.Null);
			Assert.That(ColorDistance(face.color, faceCol), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void SoftTileable_SourceUsesThemeFlatToolToggle() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeFlatToolToggle(toggle"));
		Assert.That(src, Does.Not.Contain("ThemeCheckboxToggle(toggle, face, tokens.accent, tokens.success)"));
	}

	[Test]
	public void Gen3DAndPointBilinear_SourceUseThemeFlatToolToggle() {
		string gen3d = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs")));
		string res = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "..", "Assets/_gm/Features/Settings/SceneResolution_MGR.cs")));
		Assert.That(gen3d, Does.Contain("ThemeFlatToolToggle(toggle"));
		Assert.That(res, Does.Contain("ThemeFlatToolToggle(tgl"));
	}

	[Test]
	public void FullSrnLabelStyle_SourceDoesNotHardcodeColorBlack() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/RibbonViewportFullViewOnScreen_Toggle_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFullSrnLabelStyle", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Not.Contain("tmp.color = Color.black"));
		Assert.That(body, Does.Contain("ShouldRecolorBoundChrome"));
	}

	[Test]
	public void AddonAddToggle_SourceDoesNotRoundCheckmark() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("toggle.graphic = checkImg;"));
		Assert.That(src, Does.Not.Contain("ApplyRoundedControlSprite(checkImg"));
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
