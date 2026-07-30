using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AddonCloseAndPovEnsureThemeTests {
	[Test]
	public void AddonManager_CloseGetsCompactAndClearNonFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(_closePanel_button"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_closePanel_button)"));
	}

	[Test]
	public void Multiview_RefreshPovEnsuresHitFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int fn = src.IndexOf("RefreshPovAndGridChromeSelection");
		Assert.That(fn, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(fn, System.Math.Min(900, src.Length - fn));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(_showGrid_toggle)"));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(pov)"));
	}
}
