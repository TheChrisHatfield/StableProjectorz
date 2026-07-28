using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mesh-select toggle: BoundChrome hides Checkmark bevel; selection = flat fill (LeftRibbon parity).
/// </summary>
public sealed class ClickSelectMeshesThemeCheckmarkTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
		UiRuntimeSprites.ClearLineIconCache();
	}

	[Test]
	public void ApplyThemeTokens_HidesCheckmarkUnderBoundChrome() {
		var root = new GameObject("ClickSelectTheme");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<ClickSelectMeshes_Toggle_UI>();
			var togGo = new GameObject("Select", typeof(RectTransform), typeof(Image), typeof(Toggle));
			togGo.transform.SetParent(root.transform, false);
			var face = togGo.GetComponent<Image>();
			var toggle = togGo.GetComponent<Toggle>();
			toggle.targetGraphic = face;
			var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
			checkGo.transform.SetParent(togGo.transform, false);
			var check = checkGo.GetComponent<Image>();
			toggle.graphic = check;
			check.enabled = true;

			var field = typeof(ClickSelectMeshes_Toggle_UI).GetField(
				"_selectMode_toggle", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null);
			field.SetValue(ui, toggle);

			Assert.That(SpzUiThemeOps.TryApplyTheme(
				"p1-experiment",
				new JObject {
					["control_bg"] = "#292A2EFF",
					["accent"] = "#F2CA50FF",
					["icon_tint"] = "#D0C5AFFF",
				},
				"replace",
				out string error), Is.True, error);

			var apply = typeof(ClickSelectMeshes_Toggle_UI).GetMethod(
				"ApplyThemeTokens", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(apply, Is.Not.Null);
			apply.Invoke(ui, null);

			Assert.That(check.enabled, Is.False);
			Assert.That(face.enabled, Is.True);
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
