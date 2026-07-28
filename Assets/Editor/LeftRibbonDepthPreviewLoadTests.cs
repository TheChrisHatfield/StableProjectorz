using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Project load under Nomad must clear DEP depth preview (black silhouette overlay).
/// </summary>
public sealed class LeftRibbonDepthPreviewLoadTests {

	[Test]
	public void EnsureDepthPreviewOffClearsToggleWithoutNotify() {
		var root = new GameObject("LeftRibbonDepthLoad");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<LeftRibbon_UI>();
			var togGo = new GameObject("DEP", typeof(RectTransform), typeof(Image), typeof(Toggle));
			togGo.transform.SetParent(root.transform, false);
			var toggle = togGo.GetComponent<Toggle>();
			toggle.targetGraphic = togGo.GetComponent<Image>();
			toggle.isOn = true;
			bool notified = false;
			toggle.onValueChanged.AddListener(_ => notified = true);

			SetPrivate(ui, "_toggleDepthMode_button", toggle);
			ui.EnsureDepthPreviewOff();

			Assert.That(toggle.isOn, Is.False);
			Assert.That(notified, Is.False, "SetIsOnWithoutNotify must not re-fire load side effects");
		}
		finally {
			Object.DestroyImmediate(root);
		}
	}

	static void SetPrivate(object target, string fieldName, object value) {
		var f = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, fieldName);
		f.SetValue(target, value);
	}
}
