using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Settings panel buttons with glyph faces must not take SolidSquare under Nomad.</summary>
public sealed class SettingsGlyphFaceThemeTests {
	[Test]
	public void SettingsPanel_SkipsSolidSquareOnAuthoredIconFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "Settings_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(2200, src.Length - idx));
		Assert.That(body, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(body.IndexOf("EnsureSelectableHitFace"),
			Is.LessThan(body.IndexOf("IsAuthoredIconFace(btn.targetGraphic)")));
	}
}
