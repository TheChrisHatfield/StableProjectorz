using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ValueAssistThemeLeaveWiringTests {

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
	public void ValueAssist_KeepsThemeChangedWhileDisabled_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_ValueAssistPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Do NOT unsubscribe ThemeChanged here"));
		// Scope to the OnDisable body: a fixed-width window ran past it into OnDestroy, which
		// legitimately unsubscribes, so this failed on correct code.
		string disableBody = MethodBody(src, "void OnDisable()");
		Assert.That(disableBody, Does.Not.Contain("ThemeChanged -= ApplyThemeTokens"));
	}
}
