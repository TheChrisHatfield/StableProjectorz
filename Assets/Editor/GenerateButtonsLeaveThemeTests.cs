using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class GenerateButtonsLeaveThemeTests {
	[Test]
	public void GenerateButtons_LeaveRestoresEachGenAndCancelRoot() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Layouts"", ""Viewport (MainView)"", ""GenerateButtons_Main_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""RestoreGenButton(_cancelGeneration_button)""));
		Assert.That(src, Does.Contain(""RestoreGenButton(_generateART_button)""));
		Assert.That(src, Does.Contain(""RestoreGenButton(_generate3D_button)""));
	}
}
