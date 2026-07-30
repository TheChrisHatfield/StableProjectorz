using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Art2D Import→Layer glyph must not be SolidSquare-crushed under Nomad context-menu chrome.</summary>
public sealed class Art2DImportToLayerChromeThemeTests {

	[Test]
	public void ApplyContextMenuChrome_SkipsImportToLayerBtn() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""AddonSystem"", ""SpzUiThemeOps.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ImportToLayerBtn""),
			""Context menu chrome must skip ImportToLayerBtn so SolidSquare does not blank the glyph"");
	}

	[Test]
	public void Art2DContextMenu_ThemesImportWithoutSelectableSolidSquare() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Icons"", ""IconUI"", ""IconUI_Art2D_ContextMenu.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ThemeImportToLayerButton""));
		Assert.That(src, Does.Contain(""ApplyBoundChromeGraphic""));
		// Creation path must not call Selectable SolidSquare on the glyph face.
		int ensureIx = src.IndexOf(""void EnsureImportToLayerButton"");
		Assert.That(ensureIx, Is.GreaterThanOrEqualTo(0));
		string ensureBody = src.Substring(ensureIx, System.Math.Min(1200, src.Length - ensureIx));
		Assert.That(ensureBody, Does.Not.Contain(""ApplyBoundChromeSelectable""),
			""Import glyph face must not go through ApplyBoundChromeSelectable"");
	}
}
