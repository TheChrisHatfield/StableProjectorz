using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Left ribbon selection sync must use BoundChrome gate (not hardcoded nomad-inspired id).
/// </summary>
public sealed class LeftRibbonBoundChromeGateTests {

	[Test]
	public void SyncAndSnapshot_SourceUsesShouldRecolorBoundChrome() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Viewport/Main Viewport/LeftRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzUiThemeOps.ShouldRecolorBoundChrome"));
		Assert.That(src, Does.Not.Contain("ActiveThemeId, \"nomad-inspired\""));
	}

	[Test]
	public void SnapshotNomadChromeSelection_TracksBoundChromeNotThemeId() {
		SpzUiThemeOps.ResetTheme();
		var root = new GameObject("LeftRibbonGate");
		root.SetActive(false);
		try {
			var ui = root.AddComponent<LeftRibbon_UI>();
			var snap = typeof(LeftRibbon_UI).GetMethod(
				"SnapshotNomadChromeSelection", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(snap, Is.Not.Null);
			snap.Invoke(ui, null);
			var flag = typeof(LeftRibbon_UI).GetField(
				"_lastNomadChrome", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(flag, Is.Not.Null);
			Assert.That((bool)flag.GetValue(ui), Is.EqualTo(SpzUiThemeOps.ShouldRecolorBoundChrome));
		}
		finally {
			Object.DestroyImmediate(root);
			SpzUiThemeOps.ResetTheme();
		}
	}
}
