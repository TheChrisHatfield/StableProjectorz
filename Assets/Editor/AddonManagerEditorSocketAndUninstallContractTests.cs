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
		string body = src.Substring(i, Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("AddonManagerCanvasSortOrder + 100"));
		Assert.That(body, Does.Contain("RestoreConfirmPopupSort"));
	}

	[Test]
	public void EmptyShellStatus_DistinguishesLoadingVsHttpDown() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ShouldSeedNativeAddonFallbackStatic"));
		Assert.That(src, Does.Contain("still loading"));
	}
}
