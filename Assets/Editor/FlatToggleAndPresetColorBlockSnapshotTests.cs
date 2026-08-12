using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FlatToggleAndPresetColorBlockSnapshotTests {
	[Test]
	public void ThemeFlatToolToggle_And_PresetSquare_SnapshotColorBlockBeforeWhite_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		string src = File.ReadAllText(path);

		int flat = src.IndexOf("public static void ThemeFlatToolToggle", System.StringComparison.Ordinal);
		string flatBody = src.Substring(flat, System.Math.Min(900, src.Length - flat));
		Assert.That(flatBody, Does.Contain("SnapshotAuthoredColorBlock(toggle)"));

		int preset = src.IndexOf("public static void ThemePromptPresetSquareCell", System.StringComparison.Ordinal);
		string presetBody = src.Substring(preset, System.Math.Min(1100, src.Length - preset));
		Assert.That(presetBody, Does.Contain("SnapshotAuthoredColorBlock(selectable)"));
	}
}
