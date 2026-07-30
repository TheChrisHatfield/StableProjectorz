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
