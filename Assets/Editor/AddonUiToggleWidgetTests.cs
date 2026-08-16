using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

public sealed class AddonUiToggleWidgetTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void AddToggleGetSetValueAndThemeApply() {
		var host = new GameObject("AddonUiToggleHost");
		try {
			var mgr = host.AddComponent<AddonUI_MGR>();
			// Seed registry map used by CreatePanel/AddToggle without full ribbon.
			var field = typeof(AddonUI_MGR).GetField("_addonUIElements",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			var dict = (System.Collections.IDictionary)field.GetValue(mgr);
			const string addonId = "ToggleTestAddon";
			dict[addonId] = new System.Collections.Generic.List<GameObject>();

			var panel = new GameObject("AddonPanel_ToggleTest");
			panel.transform.SetParent(host.transform, false);
			panel.AddComponent<RectTransform>();
			panel.AddComponent<Image>();
			((System.Collections.Generic.List<GameObject>)dict[addonId]).Add(panel);
			string panelId = panel.GetInstanceID().ToString();

			string elementId = mgr.AddToggle(addonId, panelId, "Demo toggle", false, null);
			Assert.That(elementId, Is.Not.Null.And.Not.Empty);
			Assert.That(mgr.GetUIElementValue(elementId), Is.EqualTo(false));
			Assert.That(mgr.SetUIElementValue(elementId, true), Is.True);
			Assert.That(mgr.GetUIElementValue(elementId), Is.EqualTo(true));

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new Newtonsoft.Json.Linq.JObject { ["accent"] = "#F2CA50", ["font_scale"] = 1.2 },
				"replace",
				out string error), Is.True, error);
			SpzUiThemeOps.ApplyToAddonUiRoot(panel);
			var toggle = panel.GetComponentInChildren<Toggle>(true);
			Assert.That(toggle, Is.Not.Null);
			Assert.That(toggle.graphic, Is.Not.Null);
			Assert.That(toggle.graphic, Is.InstanceOf<Image>());
			Assert.That(((Image)toggle.graphic).sprite, Is.Not.Null,
				"AddToggle Checkmark must have a sprite or ON state is invisible.");
		} finally {
			Object.DestroyImmediate(host);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void AddToggle_CheckmarkVisibilityMatchesInitialState() {
		var host = new GameObject("AddonUiToggleStateHost");
		try {
			var mgr = host.AddComponent<AddonUI_MGR>();
			var field = typeof(AddonUI_MGR).GetField("_addonUIElements",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var dict = (System.Collections.IDictionary)field.GetValue(mgr);
			const string addonId = "ToggleStateAddon";
			dict[addonId] = new System.Collections.Generic.List<GameObject>();

			var panel = new GameObject("AddonPanel_ToggleState");
			panel.transform.SetParent(host.transform, false);
			panel.AddComponent<RectTransform>();
			panel.AddComponent<Image>();
			((System.Collections.Generic.List<GameObject>)dict[addonId]).Add(panel);
			string panelId = panel.GetInstanceID().ToString();

			mgr.AddToggle(addonId, panelId, "Off toggle", false, null);
			mgr.AddToggle(addonId, panelId, "On toggle", true, null);

			var offToggle = panel.transform.Find("Toggle_Off toggle").GetComponent<Toggle>();
			var onToggle = panel.transform.Find("Toggle_On toggle").GetComponent<Toggle>();

			Assert.That(offToggle.isOn, Is.False);
			Assert.That(onToggle.isOn, Is.True);

			// Toggle.graphic is a plain field and OnEnable's PlayEffect runs before the checkmark exists,
			// so a persisted-ON setting would render identically to OFF unless the alpha is synced.
			Assert.That(offToggle.graphic.canvasRenderer.GetAlpha(), Is.EqualTo(0f).Within(0.001f),
				"an OFF toggle must not draw its checkmark");
			Assert.That(onToggle.graphic.canvasRenderer.GetAlpha(), Is.EqualTo(1f).Within(0.001f),
				"an ON toggle must draw its checkmark");
		} finally {
			Object.DestroyImmediate(host);
			SpzUiThemeOps.ResetTheme();
		}
	}

	[Test]
	public void BuildNotifyValueChangeRequestBody_SerializesToggleAndSlider() {
		string toggleBody = AddonUI_MGR.BuildNotifyValueChangeRequestBody("demo", "el1", "toggle", true);
		Assert.That(toggleBody, Does.Contain("\"addon_id\":\"demo\""));
		Assert.That(toggleBody, Does.Contain("\"element_id\":\"el1\""));
		Assert.That(toggleBody, Does.Contain("\"element_type\":\"toggle\""));
		Assert.That(toggleBody, Does.Contain("\"value\":true"));

		string sliderBody = AddonUI_MGR.BuildNotifyValueChangeRequestBody("demo", "el2", "slider", 0.5f);
		Assert.That(sliderBody, Does.Contain("\"element_type\":\"slider\""));
		Assert.That(sliderBody, Does.Contain("\"value\":0.5"));
	}
}
