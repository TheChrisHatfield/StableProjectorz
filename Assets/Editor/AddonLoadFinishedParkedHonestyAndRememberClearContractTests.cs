using System.IO;
using NUnit.Framework;

public sealed class AddonLoadFinishedParkedHonestyAndRememberClearContractTests {

	[Test]
	public void LoadNow_StatusWarnsWhenPanelsStillParked() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void OnLoadAddonsNow()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(1800, src.Length - i));
		Assert.That(body, Does.Contain("CountParkedAwaitingRibbonShow()"));
		Assert.That(body, Does.Contain("still off-ribbon"));
	}

	[Test]
	public void SetRememberOff_DeletesEnabledIdsJson() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public static void SetRememberEnabledAddonsPreference(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(450, src.Length - i));
		Assert.That(body, Does.Contain("DeleteKey(PrefsKeyEnabledAddonIdsJson)"));
	}

	[Test]
	public void OpenPanel_RequestsParkMigrate() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OpenPanel()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("RequestMigrateParkedPanelsNow()"));
	}

	[Test]
	public void NativeSpzGo_CompletesMissingWidgetsOnExistingPanel() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void EnsureNativeSpzGoPanel()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("EnsureNativeSpzGoMissingWidgets(existingId"));
		Assert.That(body, Does.Not.Contain("&& PanelHasNamedControlPrefix(existingPanel, \"Button_Export\"))"));
	}
}
