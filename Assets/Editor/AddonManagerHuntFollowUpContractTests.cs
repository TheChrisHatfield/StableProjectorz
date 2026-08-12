using System.IO;
using NUnit.Framework;

public sealed class AddonManagerHuntFollowUpContractTests {

	[Test]
	public void OnAddonEnabledStateChanged_RecomputesDraftDirty() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void OnAddonEnabledStateChanged(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(600, src.Length - i));
		Assert.That(body, Does.Contain("RecomputeDraftDirtyFromLive()"));
	}

	[Test]
	public void NativeNomad_CompletesMissingWidgets() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureNativeNomadMissingWidgets"));
		int i = src.IndexOf("void EnsureNativeNomadThemePanel()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Not.Contain("if (HasLiveAddonPanelWithWidgets(NomadThemeAddonId))"));
	}
}
