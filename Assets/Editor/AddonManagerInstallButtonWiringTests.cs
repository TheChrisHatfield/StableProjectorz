using System.IO;
using NUnit.Framework;

/// <summary>
/// Install from File must rebind on recovered shells and use the deferred file-browser helper.
/// </summary>
public sealed class AddonManagerInstallButtonWiringTests {

	[Test]
	public void InstallButton_RebindsOnRecoveredShell_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureHeaderActionButtonsWired"),
			"Recovered panel shells must rebind Install/Refresh listeners.");
		Assert.That(src, Does.Contain("Bind(\"InstallButton\", ref _installFromFile_button, OnInstallFromFile"),
			"InstallButton must be explicitly rebound.");
		Assert.That(src, Does.Contain("btn.onClick.RemoveAllListeners()"),
			"Install rebind must RemoveAllListeners (IL2CPP method-group RemoveListener can miss).");
		Assert.That(src, Does.Contain("EnsureHeaderActionButtonsWired();"),
			"OpenPanel / CreatePanel / Start must call the rebind.");
	}

	[Test]
	public void InstallButton_UsesDeferredFileBrowserHelper_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("AddonInstallFromFile_Helper.CoDeferredThenPickZipOrInitPy"),
			"Install must defer one frame so the browser is not opened on the same pointer-up.");
		Assert.That(src, Does.Contain("Opening install file dialog"),
			"Install click must show status so a dead picker is visible.");
		int onInstall = src.IndexOf("void OnInstallFromFile()", System.StringComparison.Ordinal);
		Assert.That(onInstall, Is.GreaterThan(0));
		string body = src.Substring(onInstall, System.Math.Min(1200, src.Length - onInstall));
		Assert.That(body, Does.Not.Contain("FileBrowser.ShowLoadDialog"),
			"Direct ShowLoadDialog on click is the unwired/dead-browser bug.");
	}

	[Test]
	public void InstallHelper_DoesNotDisableManagerRaycaster_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstallFromFile_Helper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ElevateFileBrowserCanvas"),
			"Non-Windows fallback must still elevate SimpleFileBrowser above Addon Manager.");
		Assert.That(src, Does.Contain("AbortInstallDialogAndRestoreUi"),
			"Close/Open must hide FileBrowser so GlobalClickBlocker cannot freeze the app.");
		Assert.That(src, Does.Not.Contain("SuppressOverlayRaycaster"),
			"Disabling manager GraphicRaycaster + FileBrowser GlobalClickBlocker deadlocks all clicks.");
		Assert.That(src, Does.Contain("EnsureAddonManagerCanvasRaycastersEnabled"),
			"Must re-enable AddonManager_Canvas raycasters if an older build left them off.");
	}

	[Test]
	public void InstallHelper_UsesNativeWindowsDialogOnWin_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonInstallFromFile_Helper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryNativeWindowsOpenZipOrInitPy"),
			"Windows Install must use OS GetOpenFileName so the picker is not buried under the modal.");
		Assert.That(src, Does.Contain("GetOpenFileNameW"),
			"Must call comdlg32 GetOpenFileNameW.");
		Assert.That(src, Does.Contain("UNITY_STANDALONE_WIN"),
			"Native dialog path must be gated to Windows builds.");
		Assert.That(src, Does.Contain("CommDlgExtendedError"),
			"Must distinguish user cancel from native dialog failure.");
	}

	[Test]
	public void SaveAndRemember_RebindUsesRemoveAllListeners_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int saveAt = src.IndexOf("void TryEnsureSaveSettingsButton()", System.StringComparison.Ordinal);
		Assert.That(saveAt, Is.GreaterThan(0));
		string saveBody = src.Substring(saveAt, System.Math.Min(900, src.Length - saveAt));
		Assert.That(saveBody, Does.Contain("RemoveAllListeners()"),
			"Save settings rebind must RemoveAllListeners (IL2CPP-safe).");
		Assert.That(saveBody, Does.Not.Contain("RemoveListener(OnSaveAddonSettings)"),
			"Method-group RemoveListener on Save must not remain.");
		int remAt = src.IndexOf("void TryAddRememberPreferenceRowIfMissing()", System.StringComparison.Ordinal);
		Assert.That(remAt, Is.GreaterThan(0));
		string remBody = src.Substring(remAt, System.Math.Min(1200, src.Length - remAt));
		Assert.That(remBody, Does.Contain("RemoveAllListeners()"),
			"Remember toggle rebind must RemoveAllListeners (IL2CPP-safe).");
	}
}
