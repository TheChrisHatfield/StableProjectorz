using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PromptPresetRowGapsLeaveTests {
	[Test]
	public void EnsurePromptPresetRowGaps_LeaveRefreshesParentHlg_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static void EnsurePromptPresetRowGaps", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(1200, src.Length - idx));
		Assert.That(body, Does.Contain("ApplyScaledLayoutGroup(hlg)"));
		Assert.That(body, Does.Contain("!ShouldRecolorBoundChrome"));

		int preset = src.IndexOf("public static void ThemePromptPresetSquareCell", System.StringComparison.Ordinal);
		string presetBody = src.Substring(preset, System.Math.Min(800, src.Length - preset));
		Assert.That(presetBody, Does.Contain("EnsurePromptPresetRowGaps(selectable.transform)"));
	}
}
