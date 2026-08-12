using System.IO;
using NUnit.Framework;

public sealed class AddonUnloadImmediateRibbonParkContractTests {

	[Test]
	public void UnloadAddon_HttpPath_ParksRibbonBeforePythonUnregister() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void UnloadAddon(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("RemoveAddonPanelPreservingContent(addonId)"));
		int parkAt = body.IndexOf("RemoveAddonPanelPreservingContent(addonId)", System.StringComparison.Ordinal);
		int coAt = body.IndexOf("CoPythonUnloadThenDestroyUi", System.StringComparison.Ordinal);
		Assert.That(parkAt, Is.GreaterThan(0));
		Assert.That(coAt, Is.GreaterThan(parkAt),
			"Ribbon tab must leave the strip before waiting on HTTP unregister.");
		int notifyAt = body.IndexOf("OnAddonEnabledStateChanged?.Invoke(addonId)", System.StringComparison.Ordinal);
		Assert.That(notifyAt, Is.GreaterThan(0));
		Assert.That(notifyAt, Is.LessThan(coAt),
			"Enabled-state listeners must run when isEnabled flips, not after HTTP unload.");
	}
}
