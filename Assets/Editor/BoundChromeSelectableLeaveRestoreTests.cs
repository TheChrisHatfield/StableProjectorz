using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class BoundChromeSelectableLeaveRestoreTests {
	[Test]
	public void ApplyBoundChromeSelectable_And_ThemePromptPreset_LeaveUseFullRestore_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);

		int sel = src.IndexOf("public static void ApplyBoundChromeSelectable", System.StringComparison.Ordinal);
		Assert.That(sel, Is.GreaterThan(0));
		string selBody = src.Substring(sel, System.Math.Min(900, src.Length - sel));
		Assert.That(selBody, Does.Contain("RestoreBoundChromeUnder(selectable.transform)"));
		Assert.That(selBody, Does.Not.Contain("RestoreAuthoredGraphic(selectable.targetGraphic)"));

		int preset = src.IndexOf("public static void ThemePromptPresetSquareCell", System.StringComparison.Ordinal);
		Assert.That(preset, Is.GreaterThan(0));
		string presetBody = src.Substring(preset, System.Math.Min(700, src.Length - preset));
		Assert.That(presetBody, Does.Contain("RestoreBoundChromeUnder(selectable.transform)"));
	}
}
