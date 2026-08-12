using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class NomadStackedToolCellLeaveTests {
	[Test]
	public void ApplyNomadStackedToolCell_LeaveUsesFullRestoreBoundChromeUnder_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void ApplyNomadStackedToolCell", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(cell)"));
		Assert.That(body, Does.Not.Contain("RestoreDesignFontSize(tmp, stripUppercase"));
	}
}
