using System;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Guards the blank SPZ GO ribbon path: native fallback + widget detection must stay wired.
/// </summary>
public sealed class AddonRibbonEmptyStateTests {

	[Test]
	public void AddonUiMgr_ExposesNativeFallbackForKnownAddons() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"EnsureNativeFallbackUiWhenPythonMissing",
			BindingFlags.Instance | BindingFlags.Public);
		Assert.That(method, Is.Not.Null, "EnsureNativeFallbackUiWhenPythonMissing must remain public for ribbon activation.");
		Assert.That(method.GetParameters().Length, Is.EqualTo(1));
		Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(string)));
	}

	[Test]
	public void CommandRibbon_WidgetProbeIgnoresTitleOnlyPanels() {
		var method = typeof(CommandRibbon_UI).GetMethod(
			"ShellHasAddonPanelWidgets",
			BindingFlags.Static | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null, "ShellHasAddonPanelWidgets must exist for empty-shell detection.");

		var root = new GameObject("Panel_StableProjectorzGO");
		try {
			var panel = new GameObject("AddonPanel_StableProjectorzGO_SPZ GO");
			panel.transform.SetParent(root.transform, false);
			var title = new GameObject("Title");
			title.transform.SetParent(panel.transform, false);

			bool titleOnly = (bool)method.Invoke(null, new object[] { root.transform });
			Assert.That(titleOnly, Is.False, "Title-only AddonPanel must not count as populated UI.");

			var button = new GameObject("Button_Import");
			button.transform.SetParent(panel.transform, false);
			bool withButton = (bool)method.Invoke(null, new object[] { root.transform });
			Assert.That(withButton, Is.True, "Button_* child must count as populated UI.");
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
		}
	}
}
