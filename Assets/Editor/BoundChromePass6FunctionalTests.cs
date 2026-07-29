using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 6: ThemeCheckboxToggle litmus, paint face colors, FULL SRN leave, rembg TMP, opacity snapshot.
/// </summary>
public sealed class BoundChromePass6FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeCheckboxToggle_OnBuiltin_IsNoOpPreservingHiddenGraphic() {
		SpzUiThemeOps.ResetTheme();
		Assert.That(SpzUiThemeOps.ShouldRecolorBoundChrome, Is.False);

		var go = new GameObject("Chk", typeof(RectTransform), typeof(Image), typeof(Toggle));
		try {
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var ckGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			ckGo.transform.SetParent(go.transform, false);
			var ck = ckGo.GetComponent<Image>();
			ck.enabled = false;
			toggle.graphic = ck;
			ckGo.AddComponent<SpzUiThemeHiddenGraphic>();

			SpzUiThemeOps.ThemeCheckboxToggle(toggle, Color.gray, Color.yellow, Color.cyan);

			Assert.That(ck.GetComponent<SpzUiThemeHiddenGraphic>(), Is.Not.Null);
			Assert.That(ck.enabled, Is.False);
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void ThemeCheckboxToggle_UnderNomad_EnablesAndTintsCheckmark() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["success"] = "#3DCF8EFF",
				["tab_active"] = "#343539FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("ChkN", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var ckGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			ckGo.transform.SetParent(go.transform, false);
			var ck = ckGo.GetComponent<Image>();
			ck.enabled = false;
			toggle.graphic = ck;

			Color faceCol = Color.Lerp(SpzUiThemeOps.Active.tabActive, SpzUiThemeOps.Active.accent, 0.45f);
			SpzUiThemeOps.ThemeCheckboxToggle(toggle, faceCol, SpzUiThemeOps.Active.accent, SpzUiThemeOps.Active.success);

			Assert.That(ck.enabled, Is.True);
			Assert.That(ColorDistance(ck.color, SpzUiThemeOps.Active.success), Is.LessThan(0.05f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void PaintTab_SourceUsesPaintToolFaceColorForRadiosAndMeshUnder() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Paint/PaintTab/PaintTab_CollectPaintUI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PaintToolFaceColor(t.isOn, radioOn, radioOff)"));
		Assert.That(src, Does.Contain("PaintToolFaceColor(on, meshUvUnderOn, meshUvUnderOff)"));
	}

	[Test]
	public void FullSrn_LeaveUsesRestoreAuthoredGraphicNotHardcodedBlack() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/RibbonViewportFullViewOnScreen_Toggle_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyDockFaceChrome", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(2800, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreAuthoredGraphic(label)"));
		Assert.That(body, Does.Not.Contain("label.color = Color.black"));
	}

	[Test]
	public void Gen3D_LeaveRestoresRembgTmpLabels() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs"));
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		int leave = src.IndexOf("if (!SpzUiThemeOps.ShouldRecolorBoundChrome)", apply, System.StringComparison.Ordinal);
		int themed = src.IndexOf("var t = SpzUiThemeOps.Active;", leave, System.StringComparison.Ordinal);
		string body = src.Substring(leave, themed - leave);
		Assert.That(body, Does.Contain("RestoreGraphic(_rembg_backgroundTxt)"));
		Assert.That(body, Does.Contain("RestoreGraphic(_rembg_foregroundTxt)"));
	}

	[Test]
	public void Opacity_SourceUsesApplyBoundChromeTmpUnderNomad() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI_Opacity.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeTmp(_brushOpacityText"));
	}

	static float ColorDistance(Color a, Color b) {
		return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
	}
}
