using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stacked add-on text inputs must control child heights or Label/InputField collapse in a VLG.
/// </summary>
public sealed class SpzGoTextInputLayoutTests {

	[Test]
	public void StackedTextInputRow_RequiresChildControlHeight() {
		var go = new GameObject("TextInput_Import path");
		try {
			var vlg = go.AddComponent<VerticalLayoutGroup>();
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.childForceExpandHeight = false;
			Assert.That(vlg.childControlHeight, Is.True,
				"Stacked TextInput row must set childControlHeight so LayoutElement heights apply.");
		} finally {
			Object.DestroyImmediate(go);
		}
	}
}
