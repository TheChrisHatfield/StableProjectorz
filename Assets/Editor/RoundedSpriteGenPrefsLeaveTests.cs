using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class RoundedSpriteGenPrefsLeaveTests {
	[Test]
	public void ApplyRoundedControlSprite_LeaveRestores_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void ApplyRoundedControlSprite", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(500, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreRoundedControlSpritesUnder(image.transform)"));
	}

	[Test]
	public void GenButtonFace_ResnapshotsFullAlphaBeforeSoftDisable_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "Viewport (MainView)", "GenerateButtons_Main_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyGenButtonFace", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("ResnapshotAuthoredGraphicColor(face)"));
		Assert.That(body, Does.Contain("restored.a = 1f"));
	}

	[Test]
	public void PrefsDropdownLayout_SnapshotsHeaderAndRowLe_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ApplyResponsivePrefsDropdownLayout", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(headerLE)"));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(rowLE)"));
	}
}
