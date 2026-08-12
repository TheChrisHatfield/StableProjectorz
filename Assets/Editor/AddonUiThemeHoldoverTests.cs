using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>ThemeChanged must leave/apply parked + orphan addon roots, not only AddonPanel_* in dict.</summary>
public sealed class AddonUiThemeHoldoverTests {
	[Test]
	public void ApplyThemeToAllAddonUi_CoversParkedAndOrphans() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyThemeToAllAddonUi()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(1800, src.Length - idx));
		Assert.That(body, Does.Contain("_parkedForRibbon"));
		Assert.That(body, Does.Contain("IsUnderAddonPanelRoot"));
		Assert.That(body, Does.Contain("ThemeRoot"));
	}

	[Test]
	public void QuarantineLegacy_RegistersParkedIntoAddonUIElements() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void QuarantineLegacyMidScreenFallbackRoot()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(2200, src.Length - idx));
		Assert.That(body, Does.Contain("_addonUIElements[addonId].Add(child.gameObject)"));
	}
}
