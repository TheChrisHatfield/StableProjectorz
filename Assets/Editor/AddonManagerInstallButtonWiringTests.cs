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
		int onInstall = src.IndexOf("void OnInstallFromFile()", System.StringComparison.Ordinal);
		Assert.That(onInstall, Is.GreaterThan(0));
		string body = src.Substring(onInstall, System.Math.Min(900, src.Length - onInstall));
		Assert.That(body, Does.Not.Contain("FileBrowser.ShowLoadDialog"),
			"Direct ShowLoadDialog on click is the unwired/dead-browser bug.");
	}
}
