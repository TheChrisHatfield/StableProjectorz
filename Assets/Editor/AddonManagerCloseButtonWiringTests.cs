using System.IO;
using NUnit.Framework;

/// <summary>
/// Runtime CreatePanelIfNeeded must recreate Close — clearing the ref left dimmer-only dismiss under Nomad rebuilds.
/// </summary>
public sealed class AddonManagerCloseButtonWiringTests {

	[Test]
	public void CreatePanelIfNeeded_RecreatesCloseButton() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("\"CloseButton\""),
			"CreatePanelIfNeeded must build CloseButton.");
		Assert.That(src, Does.Contain("out _closePanel_button"),
			"Close must assign _closePanel_button for Nomad theme + tooltips.");
		int create = src.IndexOf("void CreatePanelIfNeeded()", System.StringComparison.Ordinal);
		int clear = src.IndexOf("void ClearAddonManagerPanelRefs()", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		Assert.That(clear, Is.GreaterThan(create));
		string createBody = src.Substring(create, clear - create);
		Assert.That(createBody, Does.Not.Contain("_closePanel_button = null;"),
			"CreatePanelIfNeeded must not leave Close null after build.");
		Assert.That(createBody, Does.Contain("ClosePanel, new Vector2(88, 34), out _closePanel_button"),
			"Close must wire ClosePanel on create (not only Start on a wiped serialized ref).");
	}
}
