using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ClearNonFaceLeaveRestoreTests {
	[Test]
	public void ClearNonFaceRaycastsForTheme_LeaveUsesFullRestore_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void ClearNonFaceRaycastsForTheme", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(600, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(selectable.transform)"));
	}
}
