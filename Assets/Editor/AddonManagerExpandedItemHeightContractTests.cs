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
}
