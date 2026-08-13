using System.IO;
using NUnit.Framework;

/// <summary>
/// Remember row is a compact labeled rectangle — not a tiny unmarked checkbox or a wide capsule.
/// </summary>
public sealed class AddonManagerRememberCapsuleContractTests {

	[Test]
	public void RememberRow_IsCompactLabeledRectangle() {
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
		Assert.That(body, Does.Contain("RememberButtonWidth"),
			"Remember width must use the compact constant.");
		Assert.That(src, Does.Contain("const float RememberButtonWidth = 118f"),
			"Remember must stay a small rectangle (~118px), not a 210px capsule.");
		Assert.That(src, Does.Contain("const float RememberButtonHeight = 22f"));
		Assert.That(body, Does.Contain("RememberButtonLabel(rememberOn)"),
			"Remember control must show compact labeled copy.");
		Assert.That(src, Does.Contain("ThemeRememberActionButton"),
			"Theme pass must style Remember as an action button.");
		Assert.That(src, Does.Contain("LockRememberToggleSquare"),
			"Theme pass must re-lock Remember button size.");
		Assert.That(body, Does.Not.Contain("AssignSolidFaceThenMarkRounded(bgI)"),
			"Rounded markEligible stretched Remember into a large plate — use SolidRect only.");
	}
}
