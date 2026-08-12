using System.IO;
using NUnit.Framework;

/// <summary>
/// Addon Manager header must not force-expand button height under Nomad (SolidSquare capsules).
/// RibbonIconOnly must not stomp Manager header labels.
/// </summary>
public sealed class AddonManagerHeaderCapsuleContractTests {

	[Test]
	public void HeaderHlg_DoesNotForceExpandHeight() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int create = src.IndexOf("GameObject headerObj = new GameObject(\"Header\");", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		int title = src.IndexOf("GameObject titleObj = new GameObject(\"Title\");", create, System.StringComparison.Ordinal);
		string body = src.Substring(create, title - create);
		Assert.That(body, Does.Contain("childForceExpandHeight = false"));
		Assert.That(body, Does.Contain("childControlHeight = false"));
	}

	[Test]
	public void ThemeHeaderButton_IgnoresRibbonIconOnly() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int method = src.IndexOf("static void ThemeHeaderButton(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static float ResolveAuthoredHeaderButtonWidth(", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Not.Contain("RibbonIconOnlyActive"),
			"Manager header must keep text labels under Nomad ribbon_icon_only.");
		Assert.That(body, Does.Contain("bool iconOnly = false"));
	}
}
