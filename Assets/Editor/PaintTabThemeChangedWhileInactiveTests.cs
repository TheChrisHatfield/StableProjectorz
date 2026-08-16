using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PaintTabThemeChangedWhileInactiveTests {

	/// <summary>Body of the named method, brace-matched so it cannot bleed into the next method.</summary>
	static string MethodBody(string src, string signature) {
		int sig = src.IndexOf(signature, System.StringComparison.Ordinal);
		Assert.That(sig, Is.GreaterThan(0), "missing " + signature);
		int open = src.IndexOf('{', sig);
		Assert.That(open, Is.GreaterThan(0), "no body for " + signature);
		int depth = 0;
		for (int i = open; i < src.Length; i++) {
			if (src[i] == '{') depth++;
			else if (src[i] == '}' && --depth == 0)
				return src.Substring(open, i - open + 1);
		}
		Assert.Fail("unbalanced braces for " + signature);
		return "";
	}

	[Test]
	public void CollectPaintUI_KeepsThemeChangedOnDisable_UnsubOnDestroy_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Keep ThemeChanged — Leave SPZ while another ribbon tab"));
		// Scope to the OnDisable body: a fixed-width window ran past it into OnDestroy, which
		// legitimately unsubscribes, so this failed on correct code.
		string disableBody = MethodBody(src, "void OnDisable()");
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
