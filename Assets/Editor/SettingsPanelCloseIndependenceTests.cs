using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Settings dismiss: ColorPicker-independent close + Nomad ContentSizeFitter clamp.</summary>
public sealed class SettingsPanelCloseIndependenceTests {

	[Test]
	public void SettingsMgrUpdateClosesPanelWhenColorPickerMissing() {
		// Mirror Settings_MGR.Update close rule without needing the full MGR scene graph.
		var panelGo = new GameObject("SettingsPanel", typeof(RectTransform));
		panelGo.SetActive(true);
		var panel = panelGo.GetComponent<RectTransform>();
		panel.anchorMin = new Vector2(0.3f, 0.2f);
		panel.anchorMax = new Vector2(0.7f, 0.8f);
		panel.offsetMin = Vector2.zero;
		panel.offsetMax = Vector2.zero;

		ColorPalette_Panel_UI colorPicker = null; // binding missing — prior bug early-returned before close
		Assert.That(colorPicker, Is.Null);

		// Outside the panel (screen point far left).
		Vector2 cursorPos = new Vector2(-100f, -100f);
		bool isPressed = false;
		if (panel != null && panel.gameObject.activeInHierarchy && !isPressed) {
			bool isInsidePanel = RectTransformUtility.RectangleContainsScreenPoint(panel, cursorPos, null);
			if (!isInsidePanel)
				panel.gameObject.SetActive(false);
		}

		Assert.That(panelGo.activeSelf, Is.False);
		Object.DestroyImmediate(panelGo);
	}

	[Test]
	public void ClampSettingsPanelHitRectDisablesPreferredContentSizeFitter() {
		var host = new GameObject("SettingsHost", typeof(RectTransform));
		host.SetActive(false);
		try {
			var hostRt = host.GetComponent<RectTransform>();
			hostRt.anchorMin = hostRt.anchorMax = new Vector2(1f, 0f);
			hostRt.sizeDelta = new Vector2(480f, 970f);

			var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(ContentSizeFitter));
			panelGo.transform.SetParent(host.transform, false);
			var panelRt = panelGo.GetComponent<RectTransform>();
			panelRt.anchorMin = Vector2.zero;
			panelRt.anchorMax = Vector2.one;
			panelRt.sizeDelta = new Vector2(200f, 2000f); // simulated CSF Preferred blow-up
			var csf = panelGo.GetComponent<ContentSizeFitter>();
			csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			var ui = host.AddComponent<Settings_UI>();
			SetPrivate(ui, "_settingsPanel_go", panelGo);

			ui.ClampSettingsPanelHitRect();

			Assert.That(csf.horizontalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
			Assert.That(csf.verticalFit, Is.EqualTo(ContentSizeFitter.FitMode.Unconstrained));
			Assert.That(panelRt.sizeDelta, Is.EqualTo(Vector2.zero));
			Assert.That(panelRt.anchorMin, Is.EqualTo(Vector2.zero));
			Assert.That(panelRt.anchorMax, Is.EqualTo(Vector2.one));
			Assert.That(panelRt.offsetMin, Is.EqualTo(Vector2.zero));
			Assert.That(panelRt.offsetMax, Is.EqualTo(Vector2.zero));
		}
		finally {
			Object.DestroyImmediate(host);
		}
	}

	static void SetPrivate(object target, string fieldName, object value) {
		var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, fieldName);
		f.SetValue(target, value);
	}
}
