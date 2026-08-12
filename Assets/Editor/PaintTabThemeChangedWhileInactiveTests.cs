using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PaintTabThemeChangedWhileInactiveTests {
	[Test]
	public void CollectPaintUI_KeepsThemeChangedOnDisable_UnsubOnDestroy_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Keep ThemeChanged — Leave SPZ while another ribbon tab"));
		int onDisable = src.IndexOf("void OnDisable()", System.StringComparison.Ordinal);
		Assert.That(onDisable, Is.GreaterThan(0));
		string disableBody = src.Substring(onDisable, System.Math.Min(700, src.Length - onDisable));
		Assert.That(disableBody, Does.Not.Contain("ThemeChanged -= ApplyThemeTokens"));
		Assert.That(src, Does.Contain("void OnDestroy()"));
		Assert.That(src, Does.Match(@"void OnDestroy\(\)\s*\{\s*SpzUiThemeOps\.ThemeChanged -= ApplyThemeTokens"));
	}

	[Test]
	public void LayersPanel_UnsubscribesThemeChangedOnDestroyNotDisable_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Not.Contain("void OnDisable()"));
		Assert.That(src, Does.Match(@"void OnDestroy\(\)[\s\S]{0,200}?ThemeChanged -= ApplyThemeTokens"));
	}
}
