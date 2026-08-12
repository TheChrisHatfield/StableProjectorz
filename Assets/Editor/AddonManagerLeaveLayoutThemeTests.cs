using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AddonManagerLeaveLayoutThemeTests {
	[Test]
	public void AddonManager_SnapshotsRememberAndRemoveLayoutForLeave() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SnapshotLayoutElementForTheme(le)"));
		Assert.That(src, Does.Contain("SnapshotLayoutElementForTheme(removeLe)"));
		Assert.That(src, Does.Contain("_authoredHeaderChildControlHeight"));
		Assert.That(src, Does.Contain("HideMonolithUnder(_openPanel_button.transform)"));
	}
}
