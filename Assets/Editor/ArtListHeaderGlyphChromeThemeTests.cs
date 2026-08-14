using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Art list header import/AO/delete glyphs must not be SolidSquare-crushed under Nomad.
/// </summary>
public sealed class ArtListHeaderGlyphChromeThemeTests {

	[Test]
	public void ArtListHeader_SkipsSolidSquareForPreserveAspectGlyphs() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Icons", "IconUI_List_Art", "IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("protected virtual void ApplyListChromeThemeTokens");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(2800, src.Length - ix));
		Assert.That(body, Does.Contain("preserveAspect"));
		Assert.That(body, Does.Contain("UiRuntimeSprites.IsSolidRect"));
		Assert.That(body, Does.Contain("ApplyBoundChromeGraphic(glyphFace, t.iconTint)"));
		Assert.That(body, Does.Contain("ApplyBoundChromeReadableBodyTmp(label"));
		Assert.That(body, Does.Contain("IndexOf(' ')"));
	}
}
