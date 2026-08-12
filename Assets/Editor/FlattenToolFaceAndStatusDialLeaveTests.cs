using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FlattenToolFaceAndStatusDialLeaveTests {
	[Test]
	public void FlattenToolFaceImage_LeaveRestores_And_SnapshotsPreserveAspect_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void FlattenToolFaceImage", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(img.transform)"));
		Assert.That(body, Does.Contain("authoredPreserveAspect"));
	}

	[Test]
	public void LockStatusDialLayout_DelegatesToSnapshottedShowInRibbonLock_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void LockStatusDialLayout", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(300, src.Length - idx));
		Assert.That(body, Does.Contain("LockShowInRibbonDialLayout(toggle)"));
	}
}
