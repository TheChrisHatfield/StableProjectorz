using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using TMPro;
using UnityEngine;

/// <summary>
/// Negative prompt header: StripLabel tracking must not widen PROMPT into the fixed "-" glyph.
/// </summary>
public sealed class PromptHeaderPolaritySignThemeTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void PromptHeaderUsesMildTrackingNotStripSpacing() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["text_primary"] = "#E3E2E7FF",
				["font_scale"] = 1.0,
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("prompt header");
		go.SetActive(false);
		try {
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.text = "prompt";
			tmp.fontSize = 20f;
			tmp.characterSpacing = 0f;

			SpzUiThemeOps.ApplyBoundChromePromptHeaderTmp(tmp, SpzUiThemeOps.Active.textPrimary, 13f);

			Assert.That((tmp.fontStyle & FontStyles.UpperCase) != 0, Is.True);
			Assert.That(tmp.characterSpacing, Is.LessThan(8f),
				"must not use strip tracking (18) — widens into fixed '-'");
			Assert.That(tmp.characterSpacing, Is.EqualTo(2f).Within(0.01f));
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void PolaritySignKeepsZeroTrackingAndAuthoredMetricsPath() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["text_primary"] = "#E3E2E7FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("minus");
		go.SetActive(false);
		try {
			var tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.text = "-";
			tmp.fontSize = 35f;
			tmp.characterSpacing = 0f;
			tmp.outlineWidth = 0f;

			SpzUiThemeOps.ApplyBoundChromePromptPolaritySignTmp(tmp, SpzUiThemeOps.Active.textPrimary);

			Assert.That(tmp.characterSpacing, Is.EqualTo(0f).Within(0.001f));
			Assert.That(tmp.outlineWidth, Is.EqualTo(0f).Within(0.001f));
			Assert.That(tmp.color, Is.EqualTo(SpzUiThemeOps.Active.textPrimary));
			Assert.That(tmp.fontSize, Is.EqualTo(35f).Within(0.01f), "authored size must stay (not strip 13pt)");
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void BuiltinLeaveRestoresPromptHeaderAndSign() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["text_primary"] = "#E3E2E7FF" },
			"replace",
			out string error), Is.True, error);

		var root = new GameObject("NegHeader");
		root.SetActive(false);
		try {
			var promptGo = new GameObject("prompt");
			promptGo.transform.SetParent(root.transform, false);
			var prompt = promptGo.AddComponent<TextMeshProUGUI>();
			prompt.text = "prompt";
			prompt.characterSpacing = 0f;
			prompt.fontStyle = FontStyles.Normal;

			var signGo = new GameObject("sign");
			signGo.transform.SetParent(root.transform, false);
			var sign = signGo.AddComponent<TextMeshProUGUI>();
			sign.text = "-";
			sign.fontSize = 35f;

			SpzUiThemeOps.ApplyBoundChromePromptHeaderTmp(prompt, SpzUiThemeOps.Active.textPrimary, 13f);
			SpzUiThemeOps.ApplyBoundChromePromptPolaritySignTmp(sign, SpzUiThemeOps.Active.textPrimary);
			Assert.That(prompt.characterSpacing, Is.EqualTo(2f).Within(0.01f));

			SpzUiThemeOps.ResetTheme();
			SpzUiThemeOps.RestoreBoundChromeUnder(root.transform);

			Assert.That(prompt.characterSpacing, Is.EqualTo(0f).Within(0.01f));
			Assert.That((prompt.fontStyle & FontStyles.UpperCase) == 0, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}
}
