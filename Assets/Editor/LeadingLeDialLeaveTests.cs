using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class LeadingLeDialLeaveTests {
	[Test]
	public void Leading_SquareLe_CircleDial_LeaveRestore_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);

		int lead = src.IndexOf("public static void ApplyControlLineIconLeading", System.StringComparison.Ordinal);
		string leadBody = src.Substring(lead, System.Math.Min(700, src.Length - lead));
		Assert.That(leadBody, Does.Contain("RestoreBoundChromeUnder(owner)"));
		Assert.That(leadBody, Does.Contain("SnapshotToolFaceLayout(rt)"));

		int sq = src.IndexOf("public static void EnsureSquareLayoutElement", System.StringComparison.Ordinal);
		string sqBody = src.Substring(sq, System.Math.Min(500, src.Length - sq));
		Assert.That(sqBody, Does.Contain("RestorePanelWidthsUnder(le.transform)"));

		int dial = src.IndexOf("public static void EnsureCircleDialSquareLayout", System.StringComparison.Ordinal);
		string dialBody = src.Substring(dial, System.Math.Min(500, src.Length - dial));
		Assert.That(dialBody, Does.Contain("RestorePanelWidthsUnder(dial.transform)"));
	}

	[Test]
	public void ShowInRibbonDial_LocksLayoutOnlyUnderNomad_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("LockShowInRibbonDialLayout(toggle)"),
			"Enable status and ribbon radio use ring geometry lock.");
		Assert.That(src, Does.Not.Contain("LockShowInRibbonButtonLayout"),
			"Green plate button layout must stay removed.");
		Assert.That(src, Does.Contain("SnapshotToolFaceLayout(rt)"));
		int rem = src.IndexOf("static void LockRememberToggleSquare", System.StringComparison.Ordinal);
		string remBody = src.Substring(rem, System.Math.Min(600, src.Length - rem));
		Assert.That(remBody, Does.Contain("SnapshotToolFaceLayout(rt)"));
	}

	[Test]
	public void SceneResolution_RefreshFilter_UnwindsOnLeave_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "SceneResolution_MGR.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public void RefreshFilterToggleChrome", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(500, src.Length - idx));
		Assert.That(body, Does.Contain("UnwindBoundChrome(_textureFilterPoint_toggle)"));
	}
}
