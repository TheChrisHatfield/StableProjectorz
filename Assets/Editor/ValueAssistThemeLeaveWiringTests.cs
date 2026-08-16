using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ValueAssistThemeLeaveWiringTests {
	[Test]
	public void ValueAssist_KeepsThemeChangedWhileDisabled_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_ValueAssistPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Do NOT unsubscribe ThemeChanged here"));
		int onDisable = src.IndexOf("void OnDisable()", System.StringComparison.Ordinal);
		Assert.That(onDisable, Is.GreaterThan(0));
		// Scope to the OnDisable body: a fixed-width window ran past it into OnDestroy, which
		// legitimately unsubscribes, so this failed on correct code.
		int open = src.IndexOf('{', onDisable);
		int close = src.IndexOf('}', open);
		Assert.That(close, Is.GreaterThan(open));
		string disableBody = src.Substring(open, close - open);
		Assert.That(disableBody, Does.Not.Contain("ThemeChanged -= ApplyThemeTokens"));
	}
}
