using System.IO;
using NUnit.Framework;

public sealed class AddonManagerMultiExpandAndRibbonDialTests {

	[Test]
	public void PrefsExpand_AllowsMultipleOpen_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Not.Contain("CollapseOtherExpandedItems"),
			"Accordion collapse must be removed so multiple prefs can stay open.");
		Assert.That(src, Does.Contain("Allow multiple add-ons expanded"),
			"Expand click must document multi-open behavior.");
	}

	[Test]
	public void RibbonDial_UnwindsNomadHiddenFill_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("UnwindDialFillHiddenForTheme"),
			"Nomad may hide Checkmark — unwind before showing ON fill.");
		int theme = src.IndexOf("static void ThemeShowInRibbonDial(", System.StringComparison.Ordinal);
		string body = src.Substring(theme, System.Math.Min(1200, src.Length - theme));
		Assert.That(body, Does.Contain("UnwindDialFillHiddenForTheme(fill)"));
		Assert.That(body, Does.Contain("SetAlpha(isOn ? 1f : 0f)"));
		Assert.That(body, Does.Contain("CircleFilled"));
	}
}
