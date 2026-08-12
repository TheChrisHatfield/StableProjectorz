using System.IO;
using NUnit.Framework;

public sealed class AddonManagerDraftDirtyHonestyContractTests {

	[Test]
	public void ShowInRibbon_RecomputesDraftDirtyVsSnapshot() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("ribbonToggle.onValueChanged.AddListener", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("rowToggle.onValueChanged.AddListener", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("RecomputeDraftDirtyFromLive()"),
			"Flip Show-in-Ribbon back to open-time value must clear false Close warnings.");
	}

	[Test]
	public void NoOpEnableDial_DoesNotAlwaysSetDraftEnabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("do not SetDraftEnabled (that would false-dirty"));
		Assert.That(src, Does.Contain("GetDraftEnabled(id, info.isEnabled) != isOn"));
	}

	[Test]
	public void OpenPanel_SnapshotsShowInRibbonPrefsForCloseRevert() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OpenPanel()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("public void ClosePanel()", i, System.StringComparison.Ordinal);
		Assert.That(j, Is.GreaterThan(i));
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("SeedDraftFromLiveAddons()"));
		Assert.That(body, Does.Contain("SnapshotShowInRibbonPrefs()"),
			"empty snapshot makes Close-without-Save Revert a no-op");
	}

	[Test]
	public void RecomputeDraftDirty_IncludesPersistedEnableMismatch() {
		string uiPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string mgrPath = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string ui = File.ReadAllText(uiPath);
		string mgr = File.ReadAllText(mgrPath);
		Assert.That(mgr, Does.Contain("LiveEnabledSelectionDiffersFromPersisted"));
		int i = ui.IndexOf("void RecomputeDraftDirtyFromLive()", System.StringComparison.Ordinal);
		string body = ui.Substring(i, System.Math.Min(900, ui.Length - i));
		Assert.That(body, Does.Contain("LiveEnabledSelectionDiffersFromPersisted()"),
			"SoftLoad mirrors draft to live; Close must still dirty when Remember prefs are stale.");
	}
}
