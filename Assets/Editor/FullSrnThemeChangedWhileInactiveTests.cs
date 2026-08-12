using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FullSrnThemeChangedWhileInactiveTests {
	[Test]
	public void FullSrn_KeepsThemeChangedOnDisable_UnsubOnDestroy_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "RibbonViewportFullViewOnScreen_Toggle_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Keep ThemeChanged — host often disables when switching ribbon tabs"));
		int onDisable = src.IndexOf("void OnDisable()", System.StringComparison.Ordinal);
		Assert.That(onDisable, Is.GreaterThan(0));
		string disableBody = src.Substring(onDisable, System.Math.Min(900, src.Length - onDisable));
		Assert.That(disableBody, Does.Not.Contain("ThemeChanged -= ApplyThemeTokens"));
		Assert.That(src, Does.Match(@"void OnDestroy\(\)[\s\S]{0,120}?ThemeChanged -= ApplyThemeTokens"));
	}
}
