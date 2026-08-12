using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AddonPrefsBodyLayoutSnapshotTests {
	[Test]
	public void LockPreferencesBodyLayout_SnapshotsLayoutElements_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void LockPreferencesBodyLayout", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThanOrEqualTo(0), "LockPreferencesBodyLayout must exist");
		string body = src.Substring(idx, System.Math.Min(1400, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(le)"));
		Assert.That(body, Does.Contain("SnapshotLayoutElementForTheme(rowLe)"));
		Assert.That(body, Does.Contain("ApplyPreferencesCardWidthCap"),
			"Lock must re-apply half-width card cap so theme/layout cannot stretch full row.");
		Assert.That(body, Does.Contain("childForceExpandWidth = false"),
			"Prefs body HLG must not force-expand the card across the list width.");
	}

	[Test]
	public void PreferencesCard_WidthCapIsLessThanHalf_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PrefsCardWidthFrac = 0.45f"),
			"Expanded prefs card must be a little under half panel width.");
		Assert.That(src, Does.Contain("ApplyPreferencesCardWidthCap"),
			"Create + responsive + lock paths must apply the card width cap.");
		Assert.That(src, Does.Contain("flexibleWidth = 0f"),
			"Card LayoutElement must not flex to fill the PreferencesBody row.");
	}
}
