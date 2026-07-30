using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class LayersVisAndFilterToggleThemeTests {
	[Test]
	public void LayersVisibility_ColorsTargetGraphic() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Paint"", ""PaintTab"", ""PaintTab_LayersPanel_UI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""visBtn.targetGraphic as Image""));
	}

	[Test]
	public void SceneResolution_FilterToggleClearsNonFace() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Settings"", ""SceneResolution_MGR.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""ClearNonFaceRaycastsForTheme(tgl)""));
	}
}
