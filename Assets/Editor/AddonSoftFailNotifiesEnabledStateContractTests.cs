using System.IO;
using NUnit.Framework;

public sealed class AddonSoftFailNotifiesEnabledStateContractTests {

	[Test]
	public void MarkAddonLoadFailed_SoftKeepEnabled_InvokesEnabledStateChanged() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void MarkAddonLoadFailed(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int hard = src.IndexOf("addon.isEnabled = false;", i, System.StringComparison.Ordinal);
		string softRegion = src.Substring(i, hard - i);
		Assert.That(softRegion, Does.Contain("SupportsNativeUiWithoutPython"));
		Assert.That(softRegion, Does.Contain("OnAddonEnabledStateChanged?.Invoke(addonId)"));
		// Both soft-keep paths (FULL dock + native UI) must notify.
		Assert.That(System.Text.RegularExpressions.Regex.Matches(
			softRegion, @"OnAddonEnabledStateChanged\?\.Invoke\(addonId\)").Count, Is.GreaterThanOrEqualTo(2));
	}
}
