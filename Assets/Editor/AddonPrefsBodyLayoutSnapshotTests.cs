using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AddonPrefsBodyLayoutSnapshotTests {
	[Test]
	public void LockPreferencesBodyLayout_SnapshotsLayoutElements_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void LockPreferencesBodyLayout", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1100, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(le)"));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(rowLe)"));
	}
}
