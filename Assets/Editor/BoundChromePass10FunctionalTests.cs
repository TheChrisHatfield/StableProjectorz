using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 10: ThemeFlatToolToggle silo + PayMoney snapshot order (gen/options litmus).
/// </summary>
public sealed class BoundChromePass10FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void ThemeFlatToolToggle_EndsWithClearNonFaceRaycasts() {
		var go = new GameObject("SoftCell", typeof(RectTransform), typeof(Image), typeof(Toggle));
		go.SetActive(false);
		try {
			var face = go.GetComponent<Image>();
			face.raycastTarget = true;
			var toggle = go.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var label = labelGo.AddComponent<TextMeshProUGUI>();
			label.raycastTarget = true;
			var tickGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			tickGo.transform.SetParent(go.transform, false);
			var tick = tickGo.GetComponent<Image>();
			tick.raycastTarget = true;
			toggle.graphic = tick;

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
			SpzUiThemeOps.ThemeFlatToolToggle(toggle, t.controlBg, t.accent, t.textPrimary);

			Assert.That(face.raycastTarget, Is.True);
			Assert.That(label.raycastTarget, Is.False);
			Assert.That(tick.raycastTarget, Is.False);

			SpzUiThemeOps.RestoreBoundChromeUnder(go.transform);
			Assert.That(label.raycastTarget, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void PayMoney_SourceDoesNotClearRaycastBeforeTmpSnapshot() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main Viewport/PayMoney_button.cs"));
		string src = System.IO.File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("ClearNonFaceRaycastsForTheme"));
		Assert.That(body, Does.Contain("ApplyBoundChromeTmp"));
		int poison = body.IndexOf("tmp.raycastTarget = false", System.StringComparison.Ordinal);
		int tmp = body.IndexOf("ApplyBoundChromeTmp", System.StringComparison.Ordinal);
		Assert.That(poison < 0 || poison > tmp, Is.True,
			"must not clear TMP raycast before BoundChrome snapshot");
	}

	[Test]
	public void Gen3DRembg_SourceUsesClearNonFace() {
		string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs"));
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_rembg_button)"));
	}
}
