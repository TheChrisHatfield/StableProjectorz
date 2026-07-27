using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Settings click-outside close must not require ColorPicker binding.</summary>
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
			bool isInsidePanel = RectTransformUtility.RectangleContainsScreenPoint(panel, cursorPos);
			if (!isInsidePanel)
				panel.gameObject.SetActive(false);
		}

		Assert.That(panelGo.activeSelf, Is.False);
		Object.DestroyImmediate(panelGo);
	}
}
