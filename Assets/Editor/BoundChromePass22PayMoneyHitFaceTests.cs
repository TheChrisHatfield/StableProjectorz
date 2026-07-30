using System.IO;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using spz;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 22: PayMoney / thank button must Ensure hit face under Nomad (basics litmus).
/// </summary>
public sealed class BoundChromePass22PayMoneyHitFaceTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void PayMoney_SourceUsesApplyBoundChromeSelectable() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Viewport/Main Viewport/PayMoney_button.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(_button"));
		Assert.That(src, Does.Contain("ApplyBoundChromeCompactToolLabelTmp"));
		Assert.That(src, Does.Not.Contain("ApplySolidSquareChrome(_button"));
	}

	[Test]
	public void PayMoneyTheme_CompactLabelFitsWithoutOpenTracking() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"pass22-thank-compact",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2D6FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("PayMoneyCompact", typeof(RectTransform), typeof(Image), typeof(Button), typeof(PayMoney_button));
		go.SetActive(false);
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = go.GetComponent<Image>();
			var pay = go.GetComponent<PayMoney_button>();
			typeof(PayMoney_button)
				.GetField("_button", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pay, btn);
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.text = "thank";
			tmp.font = TMP_Settings.defaultFontAsset;
			tmp.enableWordWrapping = true;

			typeof(PayMoney_button)
				.GetMethod("ApplyThemeTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(pay, null);

			Assert.That(tmp.characterSpacing, Is.LessThan(4f), "thank must not use BoundChromeTmp ~10 tracking into Settings");
			Assert.That(tmp.enableWordWrapping, Is.False);
		} finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void PayMoneyTheme_EnsuresFaceWhenNullTargetGraphic() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"pass22-pay",
			new JObject {
				["control_bg"] = "#292A2EFF",
				["accent"] = "#F2CA50FF",
				["text_primary"] = "#E3E2D6FF",
			},
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("PayMoney", typeof(RectTransform), typeof(Button), typeof(PayMoney_button));
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			var pay = go.GetComponent<PayMoney_button>();
			typeof(PayMoney_button)
				.GetField("_button", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.SetValue(pay, btn);
			var labelGo = new GameObject("Label", typeof(RectTransform));
			labelGo.transform.SetParent(go.transform, false);
			var tmp = labelGo.AddComponent<TextMeshProUGUI>();
			tmp.raycastTarget = true;

			typeof(PayMoney_button)
				.GetMethod("ApplyThemeTokens", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(pay, null);

			Assert.That(btn.targetGraphic, Is.Not.Null);
			Assert.That(btn.targetGraphic.raycastTarget, Is.True);
			Assert.That(tmp.raycastTarget, Is.False);
		} finally {
			Object.DestroyImmediate(go);
		}
	}
}
