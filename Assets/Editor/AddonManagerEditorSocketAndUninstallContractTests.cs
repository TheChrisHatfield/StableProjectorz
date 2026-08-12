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
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void OnRemoveAddon(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("AddonManagerCanvasSortOrder + 100"));
		Assert.That(body, Does.Contain("RestoreConfirmPopupSort"));
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
