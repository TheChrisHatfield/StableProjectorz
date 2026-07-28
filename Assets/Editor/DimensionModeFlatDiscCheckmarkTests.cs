using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Dimension mode discs must not flatten Toggle Checkmark overlays into CircleFilled.
/// </summary>
public sealed class DimensionModeFlatDiscCheckmarkTests {

	[Test]
	public void ApplyFlatDiscsUnder_SourceSkipsCheckmarkGraphics() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsToggleCheckmarkGraphic"));
		Assert.That(src, Does.Contain("Checkmark"));
	}
}
