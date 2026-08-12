using System.IO;
using NUnit.Framework;

public sealed class AddonHttpOffNativeSeedContractTests {

	[Test]
	public void EnableAddon_HttpOff_SeedsNativeFallback() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void EnableAddon(string addonId)", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(2200, src.Length - i));
		Assert.That(body, Does.Contain("Add-on HTTP is disabled"));
		Assert.That(body, Does.Contain("EnsureNativeFallbackUiWhenPythonMissing"));
	}

	[Test]
	public void Boot_HttpOff_SeedsNativeForEnabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SeedNativeFallbacksForEnabledAddonsWhenHttpOff"));
	}
}
