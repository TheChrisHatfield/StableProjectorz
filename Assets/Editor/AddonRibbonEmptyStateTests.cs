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
		Assert.That(method.GetParameters().Length, Is.EqualTo(2));
		Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(string)));
		Assert.That(method.GetParameters()[1].ParameterType, Is.EqualTo(typeof(bool)));
	}

	[Test]
	public void AddonMgr_NativeCapableAddonsSurvivePythonLoadFailure() {
		Assert.That(Addon_MGR.SupportsNativeUiWithoutPython(Addon_MGR.StableProjectorzGoAddonId), Is.True);
		Assert.That(Addon_MGR.SupportsNativeUiWithoutPython(Addon_MGR.NomadThemeAddonId), Is.True);
		Assert.That(Addon_MGR.SupportsNativeUiWithoutPython("MeshTools"), Is.False);
		Assert.That(Addon_MGR.SupportsNativeUiWithoutPython(Addon_MGR.RibbonOnlyFullscreenAddonId), Is.False);
	}

	[Test]
	public void AddonUiMgr_NativeFallbackSupportsForceSeed() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"EnsureNativeFallbackUiWhenPythonMissing",
			BindingFlags.Instance | BindingFlags.Public);
		Assert.That(method, Is.Not.Null);
		Assert.That(method.GetParameters().Length, Is.EqualTo(2), "addonId + force must exist so load-fail can seed while HTTP PID is still alive.");
	}

	[Test]
	public void AddonMgr_SharedReadyPollUsesActiveFlagField() {
		var field = typeof(Addon_MGR).GetField(
			"_sharedAddonReadyPollActive",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(field, Is.Not.Null, "_sharedAddonReadyPollActive must gate parallel /ready polls.");
		Assert.That(field.FieldType, Is.EqualTo(typeof(bool)));
	}

	[Test]
	public void AddonMgr_ExposesReadyLivenessProbe() {
		var method = typeof(Addon_MGR).GetMethod(
			"CoProbeAddonReadyOnce",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null, "Cached /ready must re-probe via CoProbeAddonReadyOnce before short-circuit.");
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

			UnityEngine.Object.DestroyImmediate(button);
			var toggle = new GameObject("Toggle_Show");
			toggle.transform.SetParent(panel.transform, false);
			bool withToggle = (bool)method.Invoke(null, new object[] { root.transform });
			Assert.That(withToggle, Is.True, "Toggle_* child must count as populated UI (same as HasLiveAddonPanelWithWidgets).");
		} finally {
			UnityEngine.Object.DestroyImmediate(root);
		}
	}
}
