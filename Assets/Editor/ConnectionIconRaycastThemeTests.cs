using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Connection status icon must not steal open-panel clicks under Nomad.
/// </summary>
public sealed class ConnectionIconRaycastThemeTests {

	[Test]
	public void ApplyThemeTokens_SourceClearsConnectionIconRaycastUnderNomad() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/Connection/ConnectionPanel_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_connectionIcon.raycastTarget = false"));
		Assert.That(src, Does.Contain("_connectionIcon.raycastTarget = true"));
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int leave = src.IndexOf("if (!SpzUiThemeOps.ShouldRecolorBoundChrome)", apply, System.StringComparison.Ordinal);
		int clear = src.IndexOf("_connectionIcon.raycastTarget = false", apply, System.StringComparison.Ordinal);
		int restore = src.IndexOf("_connectionIcon.raycastTarget = true", apply, System.StringComparison.Ordinal);
		Assert.That(restore, Is.GreaterThan(leave));
		Assert.That(restore, Is.LessThan(clear));
	}
}
