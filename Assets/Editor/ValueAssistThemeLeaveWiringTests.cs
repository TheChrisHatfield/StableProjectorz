using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ValueAssistThemeLeaveWiringTests {
	[Test]
	public void ValueAssist_KeepsThemeChangedWhileDisabled_AndRestoresPinnedCollapse_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_ValueAssistPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Do NOT unsubscribe ThemeChanged here"));
		Assert.That(src, Does.Contain("ThemeOrRestorePinnedCollapse"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_pinnedCollapseGo.transform)"));
		int onDisable = src.IndexOf("void OnDisable()", System.StringComparison.Ordinal);
		Assert.That(onDisable, Is.GreaterThan(0));
		string disableBody = src.Substring(onDisable, System.Math.Min(450, src.Length - onDisable));
		Assert.That(disableBody, Does.Not.Contain("ThemeChanged -= ApplyThemeTokens"));
	}
}
