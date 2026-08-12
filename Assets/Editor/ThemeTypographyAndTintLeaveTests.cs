using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ThemeTypographyAndTintLeaveTests {
	[Test]
	public void PromptHeader_And_NarrowDock_LeaveRestoreNomadTypography_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);

		int hdr = src.IndexOf("public static void ApplyBoundChromePromptHeaderTmp", System.StringComparison.Ordinal);
		string hdrBody = src.Substring(hdr, System.Math.Min(500, src.Length - hdr));
		Assert.That(hdrBody, Does.Contain("RestoreNomadTypography(text)"));

		int dock = src.IndexOf("public static void ApplyBoundChromeNarrowDockLabelTmp", System.StringComparison.Ordinal);
		string dockBody = src.Substring(dock, System.Math.Min(700, src.Length - dock));
		Assert.That(dockBody, Does.Contain("RestoreNomadTypography(text)"));
		Assert.That(dockBody.IndexOf("EnsureDesignFontPt", System.StringComparison.Ordinal),
			Is.GreaterThan(dockBody.IndexOf("!ShouldRecolorBoundChrome", System.StringComparison.Ordinal)));
	}

	[Test]
	public void ApplyLineIconTint_SnapshotsAndRestores_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void ApplyLineIconTint", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(400, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotAuthoredGraphic(image)"));
		Assert.That(body, Does.Contain("RestoreAuthoredGraphic(image)"));
	}

	[Test]
	public void LeftRibbon_FlatToolColorBlock_Snapshots_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "Main Viewport", "LeftRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFlatToolColorBlock", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(400, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotAuthoredColorBlock(sel)"));
	}
}
