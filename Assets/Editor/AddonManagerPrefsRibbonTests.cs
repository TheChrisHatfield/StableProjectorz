using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Contract tests for Blender-like Add-on Manager host prefs (show_in_command_ribbon).
/// </summary>
public sealed class AddonManagerPrefsRibbonTests {

	[Test]
	public void AddonMgr_ExposesShowInCommandRibbonHostPrefKey() {
		Assert.That(Addon_MGR.PrefKeyShowInCommandRibbon, Is.EqualTo("show_in_command_ribbon"));
	}

	[Test]
	public void AddonMgr_ShouldShowInCommandRibbon_DefaultTrueWhenNoInstance() {
		Assert.That(Addon_MGR.ShouldShowInCommandRibbonStatic("MeshTools"), Is.True);
	}

	[Test]
	public void AddonMgr_ShouldShowInCommandRibbon_RibbonOnlyFullscreenAlwaysFalse() {
		Assert.That(Addon_MGR.ShouldShowInCommandRibbonStatic(Addon_MGR.RibbonOnlyFullscreenAddonId), Is.False);
	}

	[Test]
	public void AddonMgr_ExposesPrefsApis() {
		Assert.That(typeof(Addon_MGR).GetMethod("GetAddonPrefBool", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
		Assert.That(typeof(Addon_MGR).GetMethod("SetAddonPrefBool", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
		Assert.That(typeof(Addon_MGR).GetMethod("PersistAddonPrefsNow", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
		Assert.That(typeof(Addon_MGR).GetMethod("SetShowInCommandRibbon", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
		Assert.That(typeof(Addon_MGR).GetMethod("ShouldShowInCommandRibbon", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
	}

	[Test]
	public void AddonInfo_HasPrefsBagField() {
		var field = typeof(Addon_MGR.AddonInfo).GetField("prefs", BindingFlags.Instance | BindingFlags.Public);
		Assert.That(field, Is.Not.Null);
		Assert.That(field.FieldType.Name, Is.EqualTo("JObject"));
	}

	[Test]
	public void AddonUiMgr_CreatePanelGatesOnShouldShowInCommandRibbon() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ShouldShowInCommandRibbonStatic"));
		Assert.That(src, Does.Contain("forceParkHiddenRibbon"));
		Assert.That(src, Does.Contain("CountParkedAwaitingRibbonShow"));
	}

	[Test]
	public void CommandRibbon_ExposesRemovePreservingContent() {
		var method = typeof(CommandRibbon_UI).GetMethod(
			"RemoveAddonPanelPreservingContent",
			BindingFlags.Instance | BindingFlags.Public);
		Assert.That(method, Is.Not.Null);
		Assert.That(method.GetParameters().Length, Is.EqualTo(1));
	}

	[Test]
	public void AddonManagerUi_HasExpandablePreferencesAndRibbonToggle() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PreferencesButton"));
		Assert.That(src, Does.Contain("PreferencesBody"));
		Assert.That(src, Does.Contain("ShowInRibbonToggle"));
		Assert.That(src, Does.Contain("Show in Command Ribbon"));
		Assert.That(src, Does.Contain("PersistAddonPrefsNow"));
		Assert.That(src, Does.Contain("SetShowInCommandRibbon"));
	}

	[Test]
	public void AddonMgr_PrefsPersistKeyInSource() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/Addon_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("spz.addons.prefsByIdJson.v1"));
		Assert.That(src, Does.Contain("ApplyAddonPrefsFromPlayerPrefsOnFirstDiscover"));
	}

	[Test]
	public void AddonMgr_MarkAddonLoadFailed_SeedsNativeEvenWhenRibbonHidden() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/Addon_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		const string seedCall = "EnsureNativeFallbackUiWhenPythonMissing(addonId, force: true)";
		Assert.That(src, Does.Contain(seedCall));
		int seedIdx = src.IndexOf(seedCall, StringComparison.Ordinal);
		Assert.That(seedIdx, Is.GreaterThan(0));
		// Within MarkAddonLoadFailed native branch: shell gated by ribbon pref; seed always runs after.
		string window = src.Substring(Math.Max(0, seedIdx - 400), Math.Min(500, src.Length - Math.Max(0, seedIdx - 400)));
		Assert.That(window, Does.Contain("if (ShouldShowInCommandRibbon(addonId))"));
		Assert.That(window, Does.Contain("EnsureRibbonShellForEnabledAddon(addonId)"));
		Assert.That(window, Does.Contain("AddonUI_MGR.instance != null"));
	}

	[Test]
	public void AddonMgr_GetAddonPrefBoolStatic_HonorsDefaultWhenInstanceNull() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/Addon_MGR.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("if (instance == null)\r\n\t\t\t\treturn defaultValue;")
			.Or.Contain("if (instance == null)\n\t\t\t\treturn defaultValue;"));
		Assert.That(src, Does.Not.Contain(
			"return instance != null && instance.GetAddonPrefBool(addonId, key, defaultValue);"));
	}

	[Test]
	public void AddonMgr_RibbonPrefSync_OnlyWhenAddonEnabled() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/Addon_MGR.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("&& IsAddonEnabled(addonId))"));
		Assert.That(src, Does.Contain("SyncRibbonTabWithEnabledState(addonId)"));
		int syncIdx = src.IndexOf(
			"if (string.Equals(key, PrefKeyShowInCommandRibbon, StringComparison.Ordinal)",
			StringComparison.Ordinal);
		Assert.That(syncIdx, Is.GreaterThan(0));
		string window = src.Substring(syncIdx, Math.Min(280, src.Length - syncIdx));
		Assert.That(window, Does.Contain("IsAddonEnabled(addonId)"));
	}

	[Test]
	public void AddonUiMgr_CreatePanel_ForceParkStripsRibbonShellFirst() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("forceParkHiddenRibbon && commandRibbon != null"));
		Assert.That(src, Does.Contain("RemoveAddonPanelPreservingContent(addonId)"));
		int forceIdx = src.IndexOf("forceParkHiddenRibbon && commandRibbon != null", StringComparison.Ordinal);
		Assert.That(forceIdx, Is.GreaterThan(0));
		int createIdx = src.IndexOf("GetOrCreatePanelForAddon(addonId, title)", forceIdx, StringComparison.Ordinal);
		Assert.That(createIdx, Is.GreaterThan(forceIdx), "ribbon strip must run before GetOrCreate when parking");
	}
}
