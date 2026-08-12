using System.IO;
using NUnit.Framework;

public sealed class AddonManagerExpandedItemHeightContractTests {

	[Test]
	public void OpenPanel_SnapshotsRibbonPrefsOnlyWhenDraftClean() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OpenPanel()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("if (!_draftDirty)\r\n\t\t\t\tSnapshotShowInRibbonPrefs()")
			.Or.Contain("if (!_draftDirty)\n\t\t\t\tSnapshotShowInRibbonPrefs()"));
	}
}
