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
	}
}
