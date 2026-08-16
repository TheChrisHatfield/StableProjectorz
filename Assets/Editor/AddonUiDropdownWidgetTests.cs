using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Add-on dropdowns must be real dropdowns: a click opens the list of options and picking one
/// applies it. A TMP_Dropdown with no template cannot open at all — it only logs an error — so a
/// panel built in code has to supply the template itself.
/// </summary>
public sealed class AddonUiDropdownWidgetTests {

	GameObject _host;
	int _axisOrder;
	bool _flipX, _flipY, _flipZ;

	[SetUp]
	public void SetUp() {
		_axisOrder = ExportAxisSettings.AxisOrderIndex;
		_flipX = ExportAxisSettings.FlipX;
		_flipY = ExportAxisSettings.FlipY;
		_flipZ = ExportAxisSettings.FlipZ;
	}

	[TearDown]
	public void TearDown() {
		if (_host != null) Object.DestroyImmediate(_host);
		ExportAxisSettings.SetAxisOrderIndex(_axisOrder);
		ExportAxisSettings.FlipX = _flipX;
		ExportAxisSettings.FlipY = _flipY;
		ExportAxisSettings.FlipZ = _flipZ;
		SpzUiThemeOps.ResetTheme();
	}

	TMP_Dropdown BuildDropdown(string addonId, string label, List<string> options, int defaultIndex,
		out AddonUI_MGR mgr, out GameObject panel) {
		_host = new GameObject("AddonUiDropdownHost");
		var canvas = _host.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_host.AddComponent<GraphicRaycaster>();

		mgr = _host.AddComponent<AddonUI_MGR>();
		var field = typeof(AddonUI_MGR).GetField("_addonUIElements",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(field, Is.Not.Null);
		var dict = (IDictionary)field.GetValue(mgr);
		dict[addonId] = new List<GameObject>();

		panel = new GameObject("AddonPanel_DropdownTest");
		panel.transform.SetParent(_host.transform, false);
		panel.AddComponent<RectTransform>();
		panel.AddComponent<Image>();
		((List<GameObject>)dict[addonId]).Add(panel);

		string elementId = mgr.AddDropdown(addonId, panel.GetInstanceID().ToString(), label, options, defaultIndex);
		Assert.That(elementId, Is.Not.Null.And.Not.Empty, "AddDropdown must create the widget");

		var dropdown = panel.GetComponentInChildren<TMP_Dropdown>(true);
		Assert.That(dropdown, Is.Not.Null, "the row must carry a TMP_Dropdown");
		return dropdown;
	}

	[Test]
	public void Template_SatisfiesEveryRuleTmpValidates() {
		var dropdown = BuildDropdown("PlainAddon", "Pick one",
			new List<string> { "Alpha", "Beta", "Gamma" }, 0, out _, out _);

		Assert.That(dropdown.template, Is.Not.Null,
			"without a template TMP_Dropdown logs an error on click and never opens");
		Assert.That(dropdown.captionText, Is.Not.Null, "the closed row must show the current option");
		Assert.That(dropdown.itemText, Is.Not.Null, "list rows must have a label");

		// These mirror TMP_Dropdown.SetupTemplate's own validation, which silently disables the
		// control when it fails.
		var itemToggle = dropdown.template.GetComponentInChildren<Toggle>(true);
		Assert.That(itemToggle, Is.Not.Null, "the template must contain an item Toggle");
		Assert.That(itemToggle.transform, Is.Not.EqualTo(dropdown.template),
			"the item Toggle must be a child of the template, not the template itself");
		Assert.That(itemToggle.transform.parent as RectTransform, Is.Not.Null,
			"the item Toggle's parent must be a RectTransform");
		Assert.That(dropdown.itemText.transform.IsChildOf(itemToggle.transform), Is.True,
			"the item label must live under the item Toggle");

		Assert.That(dropdown.template.gameObject.activeSelf, Is.False,
			"an active template parks an empty list box under the field");
	}

	[Test]
	public void FieldCarriesOnlyOneSelectable() {
		var dropdown = BuildDropdown("PlainAddon2", "Pick one",
			new List<string> { "Alpha", "Beta" }, 0, out _, out _);
		// Two Selectables on one GameObject is unsupported by uGUI: the Button that used to fake
		// selection by cycling sat right next to TMP_Dropdown and both answered the same click.
		var selectables = dropdown.GetComponents<Selectable>();
		Assert.That(selectables.Length, Is.EqualTo(1),
			"the dropdown field must not share its GameObject with another Selectable");
		Assert.That(selectables[0], Is.InstanceOf<TMP_Dropdown>());
	}

	[Test]
	public void TmpItselfAcceptsTheTemplate() {
		var dropdown = BuildDropdown("PlainAddon3", "Pick one",
			new List<string> { "Alpha", "Beta", "Gamma" }, 1, out _, out var panel);

		// Ask TMP for its own verdict rather than re-stating its rules. SetupTemplate is the gate
		// Show() runs through: when it rejects the template the control logs an error and silently
		// never opens, which is precisely the failure this widget had. (Show() itself cannot run
		// here — it tears the popup down with Destroy, which edit mode forbids.)
		var type = typeof(TMP_Dropdown);
		var setup = type.GetMethod("SetupTemplate", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(setup, Is.Not.Null, "TMP_Dropdown.SetupTemplate is gone; re-point this test");
		var valid = type.GetField("validTemplate", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(valid, Is.Not.Null, "TMP_Dropdown.validTemplate is gone; re-point this test");

		var canvas = panel.GetComponentInParent<Canvas>();
		var args = setup.GetParameters().Length == 0 ? new object[0] : new object[] { canvas };
		setup.Invoke(dropdown, args);

		Assert.That((bool)valid.GetValue(dropdown), Is.True,
			"TMP rejected the template, so the dropdown would refuse to open");
		Assert.That(dropdown.template.gameObject.activeSelf, Is.False,
			"TMP must be able to park the template hidden again after accepting it");
	}

	[Test]
	public void PickingAFlipAppliesItToTheExportBasis() {
		var dropdown = BuildDropdown("StableProjectorzGO", ExportAxisSettings.FlipLabel,
			new List<string>(ExportAxisSettings.FlipNames), 0, out _, out _);

		int zOnly = System.Array.IndexOf(ExportAxisSettings.FlipNames, "Z");
		Assert.That(zOnly, Is.GreaterThan(0));
		dropdown.value = zOnly;

		Assert.That(ExportAxisSettings.FlipZ, Is.True, "choosing Z must flip Z");
		Assert.That(ExportAxisSettings.FlipX, Is.False);
		Assert.That(ExportAxisSettings.FlipY, Is.False);
		Assert.That(ExportAxisSettings.FlipIndex, Is.EqualTo(zOnly),
			"the persisted basis must round-trip back to the same list entry");
	}

	[Test]
	public void PickingAnAxisOrderAppliesItToTheExportBasis() {
		var dropdown = BuildDropdown("StableProjectorzGO", ExportAxisSettings.AxisOrderLabel,
			new List<string>(ExportAxisSettings.AxisOrderNames), 0, out _, out _);

		int zyx = System.Array.IndexOf(ExportAxisSettings.AxisOrderNames, "ZYX");
		Assert.That(zyx, Is.GreaterThan(0));
		dropdown.value = zyx;

		Assert.That(ExportAxisSettings.AxisOrderIndex, Is.EqualTo(zyx));
		Assert.That(ExportAxisSettings.Order, Is.EqualTo(ExportAxisSettings.AxisOrder.ZYX));
	}

	[Test]
	public void SeededSelectionComesFromThePersistedBasis() {
		ExportAxisSettings.SetFlipIndex(System.Array.IndexOf(ExportAxisSettings.FlipNames, "Y"));
		// The panel is rebuilt every session; the row must open on what the user last chose rather
		// than on the default the Python side passes in.
		var dropdown = BuildDropdown("StableProjectorzGO", ExportAxisSettings.FlipLabel,
			new List<string>(ExportAxisSettings.FlipNames), 0, out _, out _);
		Assert.That(dropdown.value, Is.EqualTo(System.Array.IndexOf(ExportAxisSettings.FlipNames, "Y")));
		Assert.That(dropdown.captionText.text, Is.EqualTo("Y"),
			"the closed row must show the persisted choice, not the first option");
	}
}
