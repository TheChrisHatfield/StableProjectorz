using System.IO;
using NUnit.Framework;

public sealed class AddonManagerExpandedItemHeightContractTests {

	[Test]
	public void ThemeAndExpand_SyncAddonItemHeightWithPrefsBody() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SyncExpandedAddonItemHeight"));
		Assert.That(src, Does.Contain("SyncExpandedAddonItemHeight(item, prefsBodyT)"));
		Assert.That(src, Does.Contain("SyncExpandedAddonItemHeight(itemObj, prefsBody.transform)"));
	}

	[Test]
	public void OpenPanel_SnapshotsRibbonPrefsOnlyWhenDraftClean() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OpenPanel()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("SnapshotShowInRibbonPrefs()"));
		// Snapshot must sit inside the !_draftDirty block with SeedDraft (not after it unconditionally).
		int dirty = body.IndexOf("if (!_draftDirty)", System.StringComparison.Ordinal);
		int snap = body.IndexOf("SnapshotShowInRibbonPrefs()", System.StringComparison.Ordinal);
		int migrate = body.IndexOf("RequestMigrateParkedPanelsNow()", System.StringComparison.Ordinal);
		Assert.That(dirty, Is.GreaterThan(0));
		Assert.That(snap, Is.GreaterThan(dirty));
		Assert.That(migrate, Is.GreaterThan(snap));
		string between = body.Substring(dirty, snap - dirty);
		Assert.That(between, Does.Contain("SeedDraftFromLiveAddons()"));
	}

	[Test]
	public void ShowInRibbonLabel_UsesEllipsisNotWrapOverflow() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int create = src.IndexOf("var ribbonLabel = ribbonLabelObj.AddComponent<TextMeshProUGUI>();", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		string createBody = src.Substring(create, System.Math.Min(450, src.Length - create));
		Assert.That(createBody, Does.Contain("enableWordWrapping = false"));
		Assert.That(createBody, Does.Contain("TextOverflowModes.Ellipsis"));
		int responsive = src.IndexOf("var label = row.Find(\"ShowInRibbonLabel\")", System.StringComparison.Ordinal);
		Assert.That(responsive, Is.GreaterThan(0));
		string respBody = src.Substring(responsive, System.Math.Min(500, src.Length - responsive));
		Assert.That(respBody, Does.Contain("enableWordWrapping = false"));
		Assert.That(respBody, Does.Contain("TextOverflowModes.Ellipsis"));
	}
}
