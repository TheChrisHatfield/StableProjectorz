using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Add-on Manager strip open launcher must get BoundChrome like Settings gear.</summary>
public sealed class AddonManagerOpenLauncherThemeTests {
	[Test]
	public void AddonManager_ThemesOpenLauncherUnderNomad() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeOpenLauncherButton"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_openPanel_button.transform)"));
		Assert.That(src, Does.Contain("ApplyControlLineIcon(_openPanel_button.transform, StudioLineIcon.Grid"));
	}
}
