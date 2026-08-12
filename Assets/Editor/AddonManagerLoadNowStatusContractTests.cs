using System.IO;
using NUnit.Framework;

/// <summary>
/// Load addons now must not report green success when loads hard-failed.
/// </summary>
public sealed class AddonManagerLoadNowStatusContractTests {

	[Test]
	public void OnLoadAddonsNow_SurfacesHardFailCounts() {
		string uiPath = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string mgrPath = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		Assert.That(File.Exists(uiPath), Is.True);
		Assert.That(File.Exists(mgrPath), Is.True);
		string ui = File.ReadAllText(uiPath);
		string mgr = File.ReadAllText(mgrPath);
		Assert.That(mgr, Does.Contain("Action<int, int> onComplete"),
			"Load-all must report requested/hardFail counts.");
		Assert.That(mgr, Does.Contain("hardFail++"),
			"Must count add-ons disabled by MarkAddonLoadFailed.");
		Assert.That(ui, Does.Not.Contain("Addons load requested"),
			"Must not always show false-success 'Addons load requested'.");
		Assert.That(ui, Does.Contain("hardFail"),
			"UI must branch status on hardFail.");
		Assert.That(ui, Does.Contain("ShowStatus(").And.Contain("false)"),
			"Hard failures must ShowStatus with isSuccess=false.");
	}
}
