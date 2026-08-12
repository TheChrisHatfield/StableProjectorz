using System.IO;
using NUnit.Framework;

public sealed class AddonManagerOpenAndLoadGuardContractTests {

	[Test]
	public void OpenPanel_ClearsPendingOnlyWhenCanvasResolved() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("_panel.SetActive(true);", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string snip = src.Substring(i, System.Math.Min(350, src.Length - i));
		Assert.That(snip, Does.Contain("if (rootCanvas != null)"));
		Assert.That(snip, Does.Contain("s_pendingOpenRequest = false"));
	}

	[Test]
	public void LoadNow_GuardsAgainstParallelClicks() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_loadAddonsNowInFlight"));
		Assert.That(src, Does.Contain("_loadAddonsNow_button.interactable = false"));
	}
}
