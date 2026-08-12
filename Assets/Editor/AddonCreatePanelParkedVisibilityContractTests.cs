using System.IO;
using NUnit.Framework;

/// <summary>
/// create_panel must report parked/visible; migrate give-up must reset when ribbon appears.
/// </summary>
public sealed class AddonCreatePanelParkedVisibilityContractTests {

	[Test]
	public void CreatePanel_SocketReportsParkedFlag() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		int create = src.IndexOf("case \"spz.ui.create_panel\":", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		int next = src.IndexOf("case \"spz.ui.add_button\":", create, System.StringComparison.Ordinal);
		string body = src.Substring(create, next - create);
		Assert.That(body, Does.Contain("IsPanelParkedOffRibbon"));
		Assert.That(body, Does.Contain("[\"parked\"]"));
		Assert.That(body, Does.Contain("[\"visible\"]"));
	}

	[Test]
	public void RibbonMigrate_ResetsAfterGiveUpWhenRibbonAppears() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsPanelParkedOffRibbon"));
		Assert.That(src, Does.Contain("Ribbon became available after migrate give-up"));
		int req = src.IndexOf("public void RequestMigrateParkedPanelsNow()", System.StringComparison.Ordinal);
		int next = src.IndexOf("void TryMigrateParkedPanelsNow()", req, System.StringComparison.Ordinal);
		string body = src.Substring(req, next - req);
		Assert.That(body, Does.Contain("_ribbonMigrateRounds = 0"),
			"Pref flip / late request must clear give-up counter.");
	}

	[Test]
	public void TryMigrateParkedPanelsNow_LeavesDisabledParkedForUnloadGetValue() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void TryMigrateParkedPanelsNow()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("IsAddonEnabledStatic(parked.addonId)"));
		Assert.That(body, Does.Not.Contain("Discarding parked panel for disabled"),
			"Migrate must not Destroy parked panels while SoftLoad HTTP unload still needs get_value.");
		Assert.That(body, Does.Contain("continue;"));
	}
}
