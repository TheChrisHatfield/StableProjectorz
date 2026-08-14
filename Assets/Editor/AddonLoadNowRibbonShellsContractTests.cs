using System.IO;
using NUnit.Framework;

/// <summary>
/// Load Now must ensure ribbon shells before POST /load_addon (same as auto-load delay path).
/// </summary>
public sealed class AddonLoadNowRibbonShellsContractTests {

	[Test]
	public void RequestLoadAllEnabledAddonsNow_EnsuresRibbonShellsFirst() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator RequestLoadAllEnabledAddonsNowCrtn(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("IEnumerator WaitForAddonServerReady(", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("EnsureRibbonShellsForAllEnabledAddons()"));
		Assert.That(body, Does.Contain("StartEnsureRibbonOnlyFullscreenViewportDock()"),
			"Load Now must ensure FULL/SRN dock when RibbonOnlyFullscreen is enabled (shells alone skip RibbonOnly).");
		int ensure = body.IndexOf("EnsureRibbonShellsForAllEnabledAddons()", System.StringComparison.Ordinal);
		int firstLoad = body.IndexOf("RequestLoadAddon(", System.StringComparison.Ordinal);
		Assert.That(ensure, Is.LessThan(firstLoad));
	}

	[Test]
	public void RememberRestore_EnsuresRibbonOnlyFullscreenDock_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("StartEnsureRibbonOnlyFullscreenViewportDock()"));
		// Restore prefs path must call dock ensure after shells.
		int restoreHint = src.IndexOf("Restored enabled add-on selection from saved preferences", System.StringComparison.Ordinal);
		Assert.That(restoreHint, Is.GreaterThan(0));
		string after = src.Substring(restoreHint, System.Math.Min(500, src.Length - restoreHint));
		Assert.That(after, Does.Contain("StartEnsureRibbonOnlyFullscreenViewportDock()"),
			"Remember restore must start FULL/SRN dock ensure when RibbonOnlyFullscreen is enabled.");
	}
}
