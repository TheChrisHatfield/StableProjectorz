using System.IO;
using NUnit.Framework;

/// <summary>
/// Nomad leave must re-tint Show-in-Ribbon dials; names must stay single-line; prefs pads snapshot first.
/// </summary>
public sealed class AddonManagerNomadWiringPassContractTests {

	[Test]
	public void ReapplyAuthored_IncludesShowInRibbonDial() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void ReapplyAuthoredStatusDialsAfterThemeRestore()", System.StringComparison.Ordinal);
		int next = src.IndexOf("void CreateAddonListItem(", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("ThemeShowInRibbonDial"),
			"Restore SPZ must re-tint Show-in-Ribbon dials or Nomad green sticks.");
	}

	[Test]
	public void ThemeAddonListItem_NameStaysSingleLine() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int theme = src.IndexOf("void ThemeAddonListItem(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static Transform FindChildRecursive(", theme, System.StringComparison.Ordinal);
		string body = src.Substring(theme, next - theme);
		Assert.That(body, Does.Not.Contain("ApplyBoundChromeReadableBodyTmp(name"),
			"ReadableBody wrap spills long names into prefs under Nomad.");
		Assert.That(body, Does.Contain("enableWordWrapping = false"));
		Assert.That(body, Does.Contain("TextOverflowModes.Ellipsis"));
	}

	[Test]
	public void ResponsivePrefs_SnapshotsLayoutGroupBeforePadWrite() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void ApplyResponsivePrefsDropdownLayout(", System.StringComparison.Ordinal);
		int next = src.IndexOf("static float MeasurePreferencesBodyHeight(", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		int snap = body.IndexOf("ApplyScaledLayoutGroup(bodyVlg)", System.StringComparison.Ordinal);
		int pad = body.IndexOf("bodyVlg.padding = new RectOffset", System.StringComparison.Ordinal);
		Assert.That(snap, Is.GreaterThan(0));
		Assert.That(pad, Is.GreaterThan(snap),
			"Must snapshot prefs VLG before responsive Nomad/narrow pads.");
	}

	[Test]
	public void RememberCopy_MatchesImmediatePersist() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("saves immediately when toggled"));
		Assert.That(src, Does.Contain("this preference saves immediately"));
	}
}
