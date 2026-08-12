using System.IO;
using NUnit.Framework;

public sealed class AddonManagerNullAddonInfoGuardContractTests {

	[Test]
	public void RefreshAddonsList_SkipsNullAddonInfo() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void RefreshAddonsList()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(2500, src.Length - i));
		Assert.That(body, Does.Contain("if (kvp.Value == null) continue;"));
	}

	[Test]
	public void CreateAddonListItem_GuardsNullAddonInfo() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void CreateAddonListItem(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("if (addonInfo == null)"));
	}

	[Test]
	public void SyncAddonRowVisual_GuardsNullAddonInfo() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void SyncAddonRowVisual(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(450, src.Length - i));
		Assert.That(body, Does.Contain("info == null"));
	}

	[Test]
	public void RefreshAddonsList_MergesRibbonSnapshotWhenDraftDirty() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureShowInRibbonSnapshotCoversAllAddons"));
		int i = src.IndexOf("public void RefreshAddonsList()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(1100, src.Length - i));
		Assert.That(body, Does.Contain("EnsureShowInRibbonSnapshotCoversAllAddons()"));
		Assert.That(body, Does.Contain("SnapshotShowInRibbonPrefs()"),
			"Clean draft refresh must re-snapshot so newly installed add-ons enter the discard baseline.");
	}
}
