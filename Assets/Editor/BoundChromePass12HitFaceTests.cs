using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 12: null targetGraphic + non-face clear must not kill Selectables under Nomad.
/// </summary>
public sealed class BoundChromePass12HitFaceTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ClearNonFace_NullTargetGraphic_DoesNotMassClearChildren() {
		var go = new GameObject("SavePlus", typeof(RectTransform), typeof(Button));
		go.SetActive(false);
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject { ["control_bg"] = "#292A2EFF", ["accent"] = "#F2CA50FF" },
				"replace",
				out string error), Is.True, error);

			SpzUiThemeOps.ClearNonFaceRaycastsForTheme(btn);
			Assert.That(label.raycastTarget, Is.True,
				"must not mass-clear when face unresolved (CommandRibbon / SAVE litmus)");
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void ApplyBoundChromeSelectable_NullTarget_CreatesHitFaceAndKeepsHittable() {
		var go = new GameObject("ResPlus", typeof(RectTransform), typeof(Button));
		go.SetActive(false);
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["text_primary"] = "#E3E2E7FF",
				},
				"replace",
				out string error), Is.True, error);

			var t = SpzUiThemeOps.Active;
			SpzUiThemeOps.ApplyBoundChromeSelectable(btn, t.controlBg, t.accent);
			SpzUiThemeOps.ApplyBoundChromeStripLabelTmp(label, t.textPrimary, 12f);

			Assert.That(btn.targetGraphic, Is.Not.Null);
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False);
			Assert.That(go.transform.Find("BoundChromeHitFace"), Is.Not.Null);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void LeftRibbonAndWorkflow_SourcesUseClearNonFaceNotPoisonLoop() {
		string left = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath, "_gm/Features/Viewport/Main Viewport/LeftRibbon_UI.cs")));
		Assert.That(left, Does.Contain("ClearNonFaceRaycastsForTheme(toggle)"));
		Assert.That(left, Does.Not.Contain("ReferenceEquals(g, toggle.targetGraphic)"));

		string wf = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/WorkflowRibbon_UI.cs")));
		Assert.That(wf, Does.Contain("ClearNonFaceRaycastsForTheme(toggle)"));
		Assert.That(wf, Does.Not.Contain("ReferenceEquals(g, toggle.targetGraphic)"));
	}
}
