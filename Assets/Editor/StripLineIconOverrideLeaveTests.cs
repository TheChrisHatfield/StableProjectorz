using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class StripLineIconOverrideLeaveTests {
	[Test]
	public void CommandRibbon_ClearsStripLineIconOverrides_OnLeave_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearStripLineIconOverridesUnder(strip)"));
		Assert.That(src, Does.Contain("static void ClearStripLineIconOverridesUnder"));
		Assert.That(src, Does.Contain("SpzStripLineIconOverride"));
	}
}
