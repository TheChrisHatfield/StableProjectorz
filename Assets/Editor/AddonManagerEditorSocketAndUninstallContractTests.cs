using System.IO;
using NUnit.Framework;

public sealed class AddonManagerEditorSocketAndUninstallContractTests {

	[Test]
	public void EditorPythonSpawn_SetsSpzSocketBound() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnvironmentVariables[\"SPZ_SOCKET_BOUND\"]"));
	}

	[Test]
	public void UninstallConfirm_RaisesAboveAddonManager() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string confirmPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "UI (reusable)", "Widgets and Gadgets", "UI_ConfirmPopup_YesNo", "ConfirmPopup_UI.cs");
		string src = File.ReadAllText(path);
		string confirm = File.ReadAllText(confirmPath);
		int i = src.IndexOf("void OnRemoveAddon(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void ShowStatus(", i, System.StringComparison.Ordinal);
		string body = end > i ? src.Substring(i, end - i) : src.Substring(i, System.Math.Min(5000, src.Length - i));
		Assert.That(body, Does.Contain("ConfirmPopup_UI.instance.Show"),
			"Uninstall must show ConfirmPopup.");
		Assert.That(body, Does.Contain("RemoveAddon(addonId"),
			"Yes must call AddonInstaller_MGR.RemoveAddon.");
		Assert.That(body, Does.Contain("refusing uninstall without confirmation"),
			"Missing ConfirmPopup must hard-fail, not silently delete the add-on.");
		Assert.That(confirm, Does.Contain("ElevateAboveAddonManagerIfOpen"),
			"ConfirmPopup.Show must elevate above AddonManager_Canvas for Uninstall/Exit clicks.");
		Assert.That(confirm, Does.Contain("RestoreElevation"),
			"ConfirmPopup must restore manager sort on Yes/No.");
		Assert.That(confirm, Does.Contain("RenderMode.ScreenSpaceOverlay"),
			"Confirm World Space canvas must become Overlay so Yes/No receive raycasts.");
	}

	[Test]
	public void RemoveAddon_WaitsForUnloadBeforeDelete() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RemoveAddonCrtn"));
		Assert.That(src, Does.Contain("UnloadAddon(addonId, () => unloadDone = true)"));
		Assert.That(src, Does.Contain("while (!unloadDone)"));
		Assert.That(src, Does.Contain("IsPythonUnloadPending(addonId)"),
			"Must not delete StreamingAssets folder while HTTP unload is only queued.");
		Assert.That(src, Does.Contain("Removal blocked"));
	}

	[Test]
	public void InstallOverwrite_UnloadsThenReEnablesWhenWasEnabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator InstallAddonCoroutine(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(4500, src.Length - i));
		Assert.That(body, Does.Contain("wasEnabledBeforeOverwrite"));
		Assert.That(body, Does.Contain("UnloadAddon(addonId, () => unloadDone = true)"));
		int unloadAt = body.IndexOf("UnloadAddon(addonId, () => unloadDone = true)", System.StringComparison.Ordinal);
		int backupAt = body.IndexOf("Backed up existing add-on", System.StringComparison.Ordinal);
		int enableAt = body.IndexOf("EnableAddon(addonId)", System.StringComparison.Ordinal);
		Assert.That(unloadAt, Is.GreaterThan(0));
		Assert.That(backupAt, Is.GreaterThan(unloadAt), "Must unload before replacing files on disk.");
		Assert.That(enableAt, Is.GreaterThan(backupAt), "Must re-enable after Discover when overwrite replaced a live add-on.");
		Assert.That(body, Does.Contain("if (wasEnabledBeforeOverwrite"));
		Assert.That(body, Does.Contain("IsPythonUnloadPending(addonId)"),
			"Zip overwrite must wait Python unload pending like RemoveAddon before Directory.Move.");
		Assert.That(body, Does.Contain("Installation blocked"),
			"Must fail install with a clear status when unload/pending times out (not hang forever).");
	}

	[Test]
	public void FolderInstall_UnloadsBeforePublish_Source() {
		string installerPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string uiPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string installer = File.ReadAllText(installerPath);
		string ui = File.ReadAllText(uiPath);
		Assert.That(installer, Does.Contain("InstallAddonFromFolder"),
			"Folder/__init__.py install must go through an unload-aware installer API.");
		Assert.That(installer, Does.Contain("InstallAddonFromFolderCrtn"),
			"Folder install must be a coroutine so UnloadAddon can complete before publish.");
		int i = installer.IndexOf("IEnumerator InstallAddonFromFolderCrtn(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = installer.Substring(i, System.Math.Min(2800, installer.Length - i));
		Assert.That(body, Does.Contain("UnloadAddon(addonId"),
			"Folder overwrite must unload before TryPublish.");
		Assert.That(body, Does.Contain("IsPythonUnloadPending(addonId)"),
			"Folder overwrite must wait Python unload pending like zip/RemoveAddon.");
		Assert.That(body, Does.Contain("wasEnabledBeforeOverwrite"),
			"Folder overwrite must re-enable when the target was live.");
		Assert.That(ui, Does.Contain("InstallAddonFromFolder"),
			"AddonManager Install .py path must use InstallAddonFromFolder, not sync TryPublish alone.");
	}

	[Test]
	public void DiscoverAddons_SkipsInstallBackupFolders_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsInstallBackupAddonFolderName"),
			"Discover must filter installer _backup_ leftover folders.");
		Assert.That(src, Does.Contain("Skipping install backup folder"),
			"Backup skip must be logged for wiring visibility.");
	}

	[Test]
	public void GetAddonIdFromRoot_OnlyParsesExplicitAddonIdAssignments() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryParseExplicitAddonIdAssignment"));
		Assert.That(src, Does.Contain("IsPlausibleAddonFolderId"));
		Assert.That(src, Does.Not.Contain("line.Contains(\"__name__\") || line.Contains(\"addon_id\") || line.Contains(\"id\")"));
		int i = src.IndexOf("public static string GetAddonIdFromRoot(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(2200, src.Length - i));
		Assert.That(body, Does.Contain("\"ADDON_ID\""));
		Assert.That(body, Does.Contain("\"addon_id\""));
	}
}
