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
		string installerPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
		string confirm = File.ReadAllText(confirmPath);
		string installer = File.ReadAllText(installerPath);
		int i = src.IndexOf("void OnRemoveAddon(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void ShowStatus(", i, System.StringComparison.Ordinal);
		string body = end > i ? src.Substring(i, end - i) : src.Substring(i, System.Math.Min(5000, src.Length - i));
		Assert.That(body, Does.Contain("ConfirmPopup_UI.instance.Show"),
			"Uninstall must show ConfirmPopup.");
		Assert.That(body, Does.Contain("CoShowUninstallConfirm"),
			"Uninstall confirm must defer one frame so the dimmer cannot eat the opening click.");
		Assert.That(body, Does.Contain("EnsureDeferredOpenCoroutineHost"),
			"Uninstall confirm must run on DDOL host so list rebuild cannot kill it.");
		Assert.That(body, Does.Contain("StopCoroutine(_uninstallConfirmCo)"),
			"Double Uninstall must stop the prior confirm coroutine.");
		Assert.That(body, Does.Contain("RemoveAddon(addonId"),
			"Yes must call AddonInstaller_MGR.RemoveAddon.");
		Assert.That(body, Does.Contain("refusing uninstall without confirmation"),
			"Missing ConfirmPopup must hard-fail, not silently delete the add-on.");
		Assert.That(src, Does.Contain("removeBtn.onClick.RemoveAllListeners()"),
			"Uninstall button must RemoveAllListeners before bind (double-fire → false cancel).");
		Assert.That(confirm, Does.Contain("ElevateForModalShow"),
			"ConfirmPopup.Show must Overlay-elevate for Settings/Uninstall/Exit clicks.");
		Assert.That(confirm, Does.Contain("RestoreElevation"),
			"ConfirmPopup must restore manager sort on Yes/No.");
		Assert.That(confirm, Does.Contain("RenderMode.ScreenSpaceOverlay"),
			"Confirm World Space canvas must become Overlay so Yes/No receive raycasts.");
		Assert.That(confirm, Does.Contain("prior acts discarded, not cancelled"),
			"Re-Show must not fire Uninstall cancelled via prior onNo.");
		Assert.That(confirm, Does.Contain("_suppressBackgroundDismissUntilPointerUp"),
			"Dimmer must ignore the opening pointer.");
		Assert.That(confirm, Does.Contain("SuppressBackgroundMaxSec"),
			"Dimmer suppress must time out so a missing Mouse.current cannot freeze the app.");
		Assert.That(confirm, Does.Contain("AbortAndRestoreUi"),
			"ConfirmPopup must expose AbortAndRestoreUi for Addon Manager Close / Exit.");
		Assert.That(confirm, Does.Contain("EnsureClickableLayout"),
			"Show must stretch authored scale-0 ConfirmPopup root so Yes/No are visible.");
		Assert.That(confirm, Does.Contain("ConfirmOverlaySortBase"),
			"Background canvas must sort above the shell so Yes receives clicks.");
		// Canvas.sortingOrder is signed 16-bit: values > 32767 wrap negative and the popup
		// renders BELOW the whole UI (invisible confirm, stuck IsShowing — the uninstall lockup).
		var sortMatch = System.Text.RegularExpressions.Regex.Match(
			confirm, @"ConfirmOverlaySortBase\s*=\s*(\d+)");
		Assert.That(sortMatch.Success, Is.True, "ConfirmOverlaySortBase must be a numeric const.");
		Assert.That(int.Parse(sortMatch.Groups[1].Value), Is.LessThanOrEqualTo(32767),
			"ConfirmOverlaySortBase must stay within Canvas sortingOrder 16-bit range (wraps negative above 32767).");
		var sortMaxMatch = System.Text.RegularExpressions.Regex.Match(
			confirm, @"ConfirmOverlaySortMax\s*=\s*(\d+)");
		Assert.That(sortMaxMatch.Success, Is.True, "ConfirmOverlaySortMax must be a numeric const.");
		Assert.That(int.Parse(sortMaxMatch.Groups[1].Value), Is.LessThanOrEqualTo(32767),
			"ConfirmOverlaySortMax must stay within Canvas sortingOrder 16-bit range.");
		Assert.That(confirm, Does.Not.Contain("Input.GetKeyDown(KeyCode.Escape);").Or.Contain("#if ENABLE_LEGACY_INPUT_MANAGER"),
			"Legacy Input calls must be guarded — project is Input System-only (activeInputHandler: 2) and unguarded calls throw per frame.");
		Assert.That(body, Does.Contain("_pendingUninstallAddonId"),
			"Duplicate Uninstall while confirm is open must not re-Show.");
		Assert.That(src, Does.Contain("AbortPendingUninstallConfirm"),
			"Exit/Settings/Close must stop deferred Uninstall so it cannot steal Exit confirm.");
		Assert.That(src, Does.Contain("AbortAndRestoreUi()"),
			"Addon Manager Close must abort a stuck elevated confirm.");
		Assert.That(confirm, Does.Contain("IsCloseProgramPrompt"),
			"Exit force-quit must only apply when Close-the-program prompt is still showing.");
		Assert.That(installer, Does.Contain("unloadTimeoutSec"),
			"RemoveAddon must not hang forever if UnloadAddon callback never fires.");
		Assert.That(installer, Does.Contain("IsPythonUnloadPending(addonId)"),
			"RemoveAddon must wait Python unload pending before Directory.Delete.");
		Assert.That(installer, Does.Contain("Directory.Delete(addonPath"),
			"Yes path must delete StreamingAssets/Addons/<id>.");
		Assert.That(installer, Does.Contain("proceeding with folder delete"),
			"Unload timeout must not permanently block Uninstall (folder delete still runs).");
	}

	[Test]
	public void RemoveAddon_WaitsForUnloadBeforeDelete() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
			Assert.That(src, Does.Contain("IsRemoveInFlight"),
				"RemoveAddon must expose in-flight guard against overlapping deletes.");
			Assert.That(src, Does.Contain("Removal already in progress"),
				"Second RemoveAddon for same id must fail fast, not start a second coroutine.");
			Assert.That(src, Does.Contain("RemoveAddonCrtn"));
			Assert.That(src, Does.Contain("UnloadAddon(addonId, () => unloadDone = true)"));
			Assert.That(src, Does.Contain("while (!unloadDone && waitUnload < unloadTimeoutSec)"),
				"Unity unload wait must be bounded (not hang forever).");
			Assert.That(src, Does.Contain("IsPythonUnloadPending(addonId)"),
				"Must wait for Python unload pending (bounded) before Directory.Delete.");
			Assert.That(src, Does.Contain("proceeding with folder delete"),
				"Timed-out unload must still delete — Uninstall must not soft-lock on HTTP down.");
			Assert.That(src, Does.Not.Contain("Removal blocked"),
				"Must not hard-fail Uninstall solely because unload timed out.");
		}

	[Test]
	public void InstallOverwrite_UnloadsThenReEnablesWhenWasEnabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstaller_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator InstallAddonCoroutine(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		// Slice to the next coroutine so the window covers the whole method — a fixed char
		// count silently truncated as the method grew and reported wiring as missing.
		int next = src.IndexOf("IEnumerator ", i + 1, System.StringComparison.Ordinal);
		string body = next > i ? src.Substring(i, next - i) : src.Substring(i);
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
		Assert.That(body, Does.Contain("proceeding with overwrite"),
			"Unload timeout must not soft-lock Install — proceed like RemoveAddon after warn.");
		Assert.That(body, Does.Not.Contain("Installation blocked"),
			"Must not hard-fail Install solely because unload timed out.");
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
		Assert.That(installer, Does.Contain("is already installed"),
			"Folder install of StreamingAssets/Addons/<id> must no-op before unload (self-path).");
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
