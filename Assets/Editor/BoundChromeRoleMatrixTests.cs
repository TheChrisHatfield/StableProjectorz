using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using spz;

/// <summary>
/// BoundChrome role matrix: traditional structure → Nomad helpers; Restore SPZ leave.
/// Spec: docs/specs/boundchrome-role-matrix/
/// </summary>
public sealed class BoundChromeRoleMatrixTests {

	static JObject Tokens(params (string key, string value)[] pairs) {
		var o = new JObject();
		foreach (var (key, value) in pairs)
			o[key] = value;
		return o;
	}

	[Test]
	public void ResolveTmpRole_DialUnderCircleSlider_IsDialValue() {
		var root = new GameObject("DialRoot");
		try {
			root.AddComponent<CircleSlider_Snapping_UI>();
			var tmpGo = new GameObject("Num", typeof(RectTransform));
			tmpGo.transform.SetParent(root.transform, false);
			var tmp = tmpGo.AddComponent<TextMeshProUGUI>();
			Assert.That(SpzUiThemeOps.ResolveTmpRole(tmp), Is.EqualTo(SpzUiThemeRole.DialValue));
		} finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ResolveTmpRole_TagOverridesHeuristic() {
		var root = new GameObject("Tagged");
		try {
			var tmp = root.AddComponent<TextMeshProUGUI>();
			var tag = root.AddComponent<SpzUiThemeRoleTag>();
			tag.role = SpzUiThemeRole.NarrowDock;
			Assert.That(SpzUiThemeOps.ResolveTmpRole(tmp), Is.EqualTo(SpzUiThemeRole.NarrowDock));
		} finally {
			Object.DestroyImmediate(root);
		}
	}

	[Test]
	public void ApplyBoundChromeRolesUnder_CompactButtonLabel_AndLeaveRestoresSpacing() {
		var root = new GameObject("MatrixRoot", typeof(RectTransform));
		var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
		btnGo.transform.SetParent(root.transform, false);
		var btn = btnGo.GetComponent<Button>();
		btn.targetGraphic = btnGo.GetComponent<Image>();
		var labelGo = new GameObject("Label", typeof(RectTransform));
		labelGo.transform.SetParent(btnGo.transform, false);
		var tmp = labelGo.AddComponent<TextMeshProUGUI>();
		tmp.characterSpacing = 0f;
		tmp.fontSize = 12f;
		tmp.enableWordWrapping = true;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF"), ("control_bg", "#292A2EFF"), ("accent", "#F2CA50FF")),
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ApplyBoundChromeRolesUnder(root.transform);
			Assert.That(SpzUiThemeOps.ResolveTmpRole(tmp), Is.EqualTo(SpzUiThemeRole.CompactTool));
			Assert.That(tmp.characterSpacing, Is.EqualTo(1f).Within(0.01f));
			Assert.That((tmp.fontStyle & FontStyles.UpperCase) != 0, Is.True);

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.ApplyBoundChromeRolesUnder(root.transform);
			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
		} finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeRolesUnder_DoesNotThemeOutsideRoot() {
		var inside = new GameObject("Inside", typeof(RectTransform));
		var outside = new GameObject("Outside", typeof(RectTransform));
		var inTmp = inside.AddComponent<TextMeshProUGUI>();
		var outTmp = outside.AddComponent<TextMeshProUGUI>();
		inTmp.characterSpacing = 0f;
		outTmp.characterSpacing = 0f;
		try {
			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				Tokens(("text_primary", "#E3E2E7FF")),
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyBoundChromeRolesUnder(inside.transform);
			Assert.That(inTmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That(outTmp.characterSpacing, Is.EqualTo(0f).Within(0.01f));
		} finally {
			Object.DestroyImmediate(inside);
			Object.DestroyImmediate(outside);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PilotSources_CallApplyBoundChromeRolesUnder() {
		string sd = File.ReadAllText(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/Input Panel/SD_InputPanel_UI.cs"));
		string cn = File.ReadAllText(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/Controlnet/ControlNetUnit_UI.cs"));
		string soft = File.ReadAllText(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs"));
		Assert.That(sd, Does.Contain("ApplyBoundChromeRolesUnder"));
		Assert.That(cn, Does.Contain("ApplyBoundChromeRolesUnder"));
		Assert.That(soft, Does.Contain("ApplyBoundChromeRolesUnder"));
	}
}
