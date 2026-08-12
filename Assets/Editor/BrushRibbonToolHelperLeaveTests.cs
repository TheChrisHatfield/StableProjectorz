using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class BrushRibbonToolHelperLeaveTests {
	[Test]
	public void ThemeToolToggle_Button_ContentSafe_LeaveRestore_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		string src = File.ReadAllText(path);
		foreach (string name in new[] {
			"static void ThemeToolToggle",
			"static void ThemeToolButton",
			"static void ThemeContentSafeHitOnly",
		}) {
			int idx = src.IndexOf(name, System.StringComparison.Ordinal);
			Assert.That(idx, Is.GreaterThan(0), name);
			string body = src.Substring(idx, System.Math.Min(500, src.Length - idx));
			Assert.That(body, Does.Contain("RestoreBoundChromeUnder"), name);
		}
	}
}
