using System.IO;
using NUnit.Framework;

/// <summary>
/// Remember row is a labeled action button — not a tiny unmarked corner checkbox.
/// </summary>
public sealed class AddonManagerRememberCapsuleContractTests {

	[Test]
	public void RememberRow_IsLabeledActionButton_NotTinyCornerCheckbox() {
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
			"Remember HLG must not control height.");
		Assert.That(body, Does.Contain("Remember next launch"),
			"Remember control must show readable button copy.");
		Assert.That(body, Does.Contain("preferredWidth = 210f"),
			"Remember is a labeled button, not a 22px unmarked square.");
		Assert.That(src, Does.Contain("ThemeRememberActionButton"),
			"Theme pass must style Remember as an action button.");
		Assert.That(src, Does.Contain("LockRememberToggleSquare"),
			"Theme pass must re-lock Remember button size.");
	}
}
