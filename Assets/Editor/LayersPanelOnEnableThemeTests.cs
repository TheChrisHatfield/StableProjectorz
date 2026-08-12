using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Layers panel must re-apply BoundChrome when re-enabled after a theme change.</summary>
public sealed class LayersPanelOnEnableThemeTests {
	[Test]
	public void LayersPanel_OnEnableAppliesThemeTokens() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void OnEnable()", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(idx, System.Math.Min(400, src.Length - idx));
		Assert.That(body, Does.Contain("ThemeChanged += ApplyThemeTokens"));
		Assert.That(body, Does.Contain("ApplyThemeTokens();"));
	}
}
