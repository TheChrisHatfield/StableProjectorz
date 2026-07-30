using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Add-on Manager chrome and rows must wire <see cref="spz.CanShowTooltip_UI"/>.</summary>
public sealed class AddonManagerTooltipTests {

	[Test]
	public void AddonManagerUi_AttachesCanShowTooltipOnChromeAndRows() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CanShowTooltip_UI"));
		Assert.That(src, Does.Contain("static void AttachTooltip"));
		Assert.That(src, Does.Contain("EnsureChromeTooltips"));
		Assert.That(src, Does.Contain("Install an add-on from a .zip"));
		Assert.That(src, Does.Contain("Persist enabled add-ons and Preferences"));
		Assert.That(src, Does.Contain("Show in Command Ribbon"));
		Assert.That(src, Does.Contain("Expand host preferences"));
		Assert.That(src, Does.Contain("Enable or disable this add-on"));
		// Start must tip chrome without requiring OpenPanel.
		int startIdx = src.IndexOf("void Start()", StringComparison.Ordinal);
		Assert.That(startIdx, Is.GreaterThan(0));
		string startWindow = src.Substring(startIdx, Math.Min(700, src.Length - startIdx));
		Assert.That(startWindow, Does.Contain("EnsureChromeTooltips()"));
		Assert.That(src, Does.Contain("EnsureRememberRowTooltip"));
		Assert.That(src, Does.Contain("Full-row hover target"));
		Assert.That(src, Does.Contain("Preferences such as Show in Command Ribbon are always saved"));
	}
}
