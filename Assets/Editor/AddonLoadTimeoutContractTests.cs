using System.IO;
using NUnit.Framework;

public sealed class AddonLoadTimeoutContractTests {

	[Test]
	public void RequestLoadAddon_UsesLongRegisterBudget() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int loadAt = src.IndexOf("IEnumerator RequestLoadAddon(string addonId, int epoch)", System.StringComparison.Ordinal);
		Assert.That(loadAt, Is.GreaterThan(0));
		string body = src.Substring(loadAt, System.Math.Min(2200, src.Length - loadAt));
		int unloadAt = body.IndexOf("IEnumerator RequestUnloadAddon", System.StringComparison.Ordinal);
		if (unloadAt > 0)
			body = body.Substring(0, unloadAt);
		Assert.That(body, Does.Contain("req.timeout = 120"),
			"Python register()/create_panel can exceed the old 8s UnityWebRequest budget.");
		Assert.That(body, Does.Not.Contain("req.timeout = 8"));
	}
}
