using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// GEN ART click wiring vs OG: soft interactable must not swallow the Hub path (Ctrl+G parity).
/// </summary>
public sealed class GenerateButtonsGenArtWiringTests {

	[TearDown]
	public void TearDown() {
		GenerateButtons_UI.OnGenerateArtButton = null;
		GenerateButtons_UI.OnGenerateBG_Button = null;
	}

	[Test]
	public void GenArtClickInvokesActionEvenWhenSoftInteractableFalse() {
		var go = new GameObject("GenArtWiringTest");
		go.SetActive(false);
		try {
			var ui = go.AddComponent<GenerateButtons_Main_UI>();
			SetStaticSoftInteractable("_genArt_button_interactable", false);

			bool invoked = false;
			GenerateButtons_UI.OnGenerateArtButton = () => invoked = true;

			var m = typeof(GenerateButtons_UI).GetMethod("OnButton_GenArt_if_allowed",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(m, Is.Not.Null);
			m.Invoke(ui, null);

			Assert.That(invoked, Is.True,
				"GEN ART click must reach OnGenerateArtButton when soft-gated (matches Ctrl+G → Hub.DenyWithMessage).");
		}
		finally {
			Object.DestroyImmediate(go);
			GenerateButtons_UI.OnGenerateArtButton = null;
		}
	}

	[Test]
	public void GenBgClickInvokesActionEvenWhenSoftInteractableFalse() {
		var go = new GameObject("GenBgWiringTest");
		go.SetActive(false);
		try {
			var ui = go.AddComponent<GenerateButtons_Main_UI>();
			SetStaticSoftInteractable("_genBG_button_interactable", false);

			bool invoked = false;
			GenerateButtons_UI.OnGenerateBG_Button = () => invoked = true;

			var m = typeof(GenerateButtons_UI).GetMethod("OnButton_GenBG_if_allowed",
				BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(m, Is.Not.Null);
			m.Invoke(ui, null);

			Assert.That(invoked, Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
			GenerateButtons_UI.OnGenerateBG_Button = null;
		}
	}

	static void SetStaticSoftInteractable(string fieldName, bool value) {
		var f = typeof(GenerateButtons_UI).GetField(fieldName,
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(f, Is.Not.Null, fieldName);
		f.SetValue(null, value);
	}
}
