using System.IO;
using NUnit.Framework;

/// <summary>
/// Settings → Show external process windows / Open WebUI in browser must apply in-session
/// (PlayerPrefs + live apply), not only after restarting StableProjectorz.
/// </summary>
public sealed class ExternalProcessSettingsInSessionContractTests {
	static string Read(params string[] parts) {
		return File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));
	}

	[Test]
	public void SettingsMgr_WiresInSessionApply_OnExternalWindowsAndBrowserToggles() {
		string src = Read("Assets", "_gm", "Features", "Settings", "Settings_MGR.cs");
		Assert.That(src, Does.Contain("_settingsInSessionApplyEnabled"));
		Assert.That(src, Does.Contain("LaunchWebUIBatFile.ApplyExternalProcessWindowsSettingInSession"));
		Assert.That(src, Does.Contain("LaunchWebUIBatFile.ApplyOpenBrowserSettingInSession"));
		Assert.That(src, Does.Contain("_settingsInSessionApplyEnabled = true"));
	}

	[Test]
	public void LaunchWebUI_HasInSessionApply_ForBrowserAndExternalWindows() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Webui", "Launch_WebUI_bat_File.cs");
		Assert.That(src, Does.Contain("ApplyOpenBrowserSettingInSession"));
		Assert.That(src, Does.Contain("ApplyExternalProcessWindowsSettingInSession"));
		Assert.That(src, Does.Contain("OpenWebUiInBrowserNow"));
		// Ready path must honor live prefs (toggle during wait).
		int i = src.IndexOf("void TryOpenBrowserWhenReady()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("void OpenWebUiInBrowserNow()", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("WebUI_OpenBrowserOnStartup"));
	}

	[Test]
	public void AddonMgr_HasInSessionExternalWindowsApply() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		Assert.That(src, Does.Contain("ApplyExternalProcessWindowsSettingInSession"));
		Assert.That(src, Does.Contain("TryGetListeningPidsOnPort"));
		// Visibility restart must reload enabled add-ons (new Python process is empty).
		int i = src.IndexOf("public void ApplyExternalProcessWindowsSettingInSession", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("OnPythonStdout_LogToUnity", i, System.StringComparison.Ordinal);
		if (j < 0) j = Math.Min(src.Length, i + 2500);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("RequestLoadEnabledAddonsAfterDelay"));
	}

	[Test]
	public void StartExternalProcess_CanToggleWindowsForLivePids() {
		string src = Read("Assets", "_gm", "_Core", "IO", "IL2cppStartProcess", "StartExternalProcess.cs");
		Assert.That(src, Does.Contain("TrySetWindowsVisibleForProcessIds"));
		Assert.That(src, Does.Contain("ShowWindow"));
		Assert.That(src, Does.Contain("EnumWindows"));
	}
}
