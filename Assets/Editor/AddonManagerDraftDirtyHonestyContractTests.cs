using System.IO;
using NUnit.Framework;

public sealed class AddonManagerDraftDirtyHonestyContractTests {

	[Test]
	public void ShowInRibbon_MarksDraftDirty() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("ribbonToggle.onValueChanged.AddListener", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("rowToggle.onValueChanged.AddListener", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("_draftDirty = true"));
	}

	[Test]
	public void NoOpEnableDial_DoesNotAlwaysSetDraftEnabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("do not SetDraftEnabled (that would false-dirty"));
		Assert.That(src, Does.Contain("GetDraftEnabled(id, info.isEnabled) != isOn"));
	}
}
