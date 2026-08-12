using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ControlNetImagesDisplayThemeLeaveTests {
	[Test]
	public void ImagesDisplay_SubscribesThemeChanged_ApplyContextMenuChrome_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_ImagesDisplay.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzUiThemeOps.ThemeChanged += ApplyThemeTokens"));
		Assert.That(src, Does.Contain("SpzUiThemeOps.ThemeChanged -= ApplyThemeTokens"));
		Assert.That(src, Does.Contain("void ApplyThemeTokens()"));
		Assert.That(src, Does.Contain("ApplyContextMenuChrome(_contextMenu_gameObj)"));
	}
}
