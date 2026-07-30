using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class Icon3dGenExportGlyphChromeThemeTests {

	[Test]
	public void Icon3dContextMenu_KeepsPreserveAspectGenExportOutOfSolidSquare() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Icon3D_ContextMenu.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemeOrRestoreGenExportButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(1400, src.Length - ix));
		Assert.That(body, Does.Contain("preserveAspect"));
		Assert.That(body, Does.Contain("ApplyBoundChromeGraphic(face, t.iconTint)"));
		Assert.That(body, Does.Contain("UiRuntimeSprites.IsSolidRect"));
	}
}

public sealed class GenerateCancelDockChromeThemeTests {

	[Test]
	public void GenerateButtons_CancelDeleteUseThemeDockChromeButton() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "Viewport (MainView)", "GenerateButtons_Main_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeDockChromeButton(_cancelGeneration_button, t.danger, t, labelSize: 12f)"));
		Assert.That(src, Does.Contain("static void ThemeDockChromeButton"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(btn)"));
	}
}

public sealed class ControlNetThumbCloseGlyphChromeThemeTests {

	[Test]
	public void CnThumbClose_SkipsSolidSquareForPreserveAspectGlyph() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_Thumb_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("void ApplyThemeTokens()");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(2200, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(_closeButton)"));
		Assert.That(body, Does.Contain("UiRuntimeSprites.IsSolidRect(closeGlyph.sprite)"));
	}
}
