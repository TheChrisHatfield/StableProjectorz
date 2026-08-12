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

	[Test]
	public void TryCreateRibbonShellNow_MigratesParkedAfterShellReady() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("bool TryCreateRibbonShellNow(string addonId)", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("void StartEnsureRibbonShellWhenReady", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("RequestMigrateParkedPanelsNow()"),
			"Late CoEnsure shell must migrate parked create_panel widgets");
		Assert.That(body, Does.Contain("parked create_panel"));
	}

	[Test]
	public void ActivateAddonShell_RetriesMigrateWhenEmpty() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void ActivateAddonShellContentOrPlaceholder", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("RequestMigrateParkedPanelsNow()"));
		Assert.That(body.IndexOf("RequestMigrateParkedPanelsNow()", System.StringComparison.Ordinal),
			Is.LessThan(body.IndexOf("EnsureNativeFallbackUiWhenPythonMissing", System.StringComparison.Ordinal)));
	}
}
