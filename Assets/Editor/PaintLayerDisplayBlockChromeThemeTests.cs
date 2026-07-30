using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PaintLayerDisplayBlockChromeThemeTests {
	[Test]
	public void ContentBearingPaintButton_SkipsDisplayBlockAndSoftAlpha() {
		string path = Path.Combine(Application.dataPath, ""_gm"", ""Features"", ""Paint"", ""PaintTab"", ""PaintTab_CollectPaintUI.cs"");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(""DisplayBlock""));
		Assert.That(src, Does.Contain(""0.15f""),
			""Soft rename plates ship ~0.12a — threshold must stay above that or SolidSquare covers layer names"");
	}
}
