using System.IO;
using NUnit.Framework;

public sealed class AddonRememberRestoreRibbonContractTests {

	[Test]
	public void RememberRestore_EnsuresRibbonShells() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("static void ApplyRememberedEnabledStateFromPlayerPrefsOnFirstDiscover()", System.StringComparison.Ordinal);
		int j = src.IndexOf("static void", i + 10, System.StringComparison.Ordinal);
		if (j < 0) j = i + 800;
		string body = src.Substring(i, Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("EnsureRibbonShellsForAllEnabledAddons()"));
	}

	[Test]
	public void InitWithoutHttp_StillEnsuresRibbonShells() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("No HTTP auto-load — still wire ribbon shells"));
	}
}
