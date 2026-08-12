using System.IO;
using NUnit.Framework;

/// <summary>
/// Remember row must not force-expand height — Nomad SolidSquare stretched it into a capsule.
/// </summary>
public sealed class AddonManagerRememberCapsuleContractTests {

	[Test]
	public void RememberRow_DoesNotForceExpandHeight() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int build = src.IndexOf("GameObject BuildRememberEnabledPreferenceRow(", System.StringComparison.Ordinal);
		Assert.That(build, Is.GreaterThan(0));
		int next = src.IndexOf("void EnsureRememberRowTooltip(", build, System.StringComparison.Ordinal);
		string body = src.Substring(build, next - build);
		Assert.That(body, Does.Contain("childForceExpandHeight = false"),
			"Remember HLG must not force-expand height under Nomad.");
		Assert.That(body, Does.Contain("childControlHeight = false"),
			"Remember HLG must not control height (protects square toggle).");
		Assert.That(body, Does.Contain("preferredWidth = 22f"),
			"Remember toggle must be a locked square.");
		Assert.That(src, Does.Contain("LockRememberToggleSquare"),
			"Theme pass must re-lock square after ThemeCheckboxToggle.");
	}
}
