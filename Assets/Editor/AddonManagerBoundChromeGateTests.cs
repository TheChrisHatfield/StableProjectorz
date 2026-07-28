using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Add-on Manager chrome must gate on BoundChrome, not a hardcoded nomad-inspired theme id.
/// </summary>
public sealed class AddonManagerBoundChromeGateTests {

	[Test]
	public void AddonManagerTheme_SourceUsesShouldRecolorBoundChromeNotHardcodedNomadId() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/AddonManager_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ShouldRecolorBoundChrome"));
		Assert.That(src, Does.Not.Contain("ActiveThemeId, \"nomad-inspired\""));
	}
}
