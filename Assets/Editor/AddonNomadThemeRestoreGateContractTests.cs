using System;
using System.IO;
using NUnit.Framework;

public sealed class AddonNomadThemeRestoreGateContractTests {

	[Test]
	public void CoRestore_GatesNomadOnAddonEnabledAndClearsWhenDisabled() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("CoRestorePersistedThemeNextFrame()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, Math.Min(1400, src.Length - i));
		Assert.That(body, Does.Contain("NomadThemeSPZ"));
		Assert.That(body, Does.Contain("ClearPersistedTheme"));
		Assert.That(body, Does.Contain("ComposeNomadStripIconsNative()"));
		Assert.That(body, Does.Contain("IsAddonEnabledStatic(\"NomadThemeSPZ\")"));
	}
}
