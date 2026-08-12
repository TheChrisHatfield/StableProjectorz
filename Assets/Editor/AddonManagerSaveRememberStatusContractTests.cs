using System.IO;
using NUnit.Framework;

/// <summary>
/// Save settings must not claim next-launch selection restore when Remember is off.
/// </summary>
public sealed class AddonManagerSaveRememberStatusContractTests {

	[Test]
	public void OnSaveAddonSettings_StatusReflectsRememberPreference() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void OnSaveAddonSettings()", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("bool GetDraftEnabled(", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("GetRememberEnabledAddonsPreference()"),
			"Save status must read Remember before claiming next-launch selection restore.");
		Assert.That(body, Does.Contain("Remember off"),
			"When Remember is off, status must not claim selection restore.");
		Assert.That(body, Does.Contain("rememberOn"),
			"Status copy must branch on rememberOn.");
	}
}
