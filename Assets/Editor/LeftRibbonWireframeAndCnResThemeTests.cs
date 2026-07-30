using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class LeftRibbonWireframeAndCnResThemeTests {
	[Test]
	public void LeftRibbon_WireframeClearsNonFaceAndLeaveRestores() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Viewport"", ""Main Viewport"", ""LeftRibbon_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ClearNonFaceRaycastsForTheme(btn)""));
		Assert.That(src, Does.Contain(""RestoreBoundChromeUnder(_toggleWireframe.transform)""));
	}

	[Test]
	public void ControlNetResRadio_ClearsNonFace() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""StableDiffusion"", ""Controlnet"", ""ControlnetPreprocessor_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ClearNonFaceRaycastsForTheme(toggle)""));
	}
}
