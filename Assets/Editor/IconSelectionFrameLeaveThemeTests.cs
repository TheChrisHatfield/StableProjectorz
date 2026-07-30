using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class IconSelectionFrameLeaveThemeTests {
	[Test]
	public void SelectionFrame_LeaveReappliesLastFrameShow() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Icons"", ""IconUI"", ""IconUI_SelectionFrame.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""_lastFrameShow""));
		Assert.That(src, Does.Contain(""ToggleFrame(_lastFrameShow)""));
	}
}
