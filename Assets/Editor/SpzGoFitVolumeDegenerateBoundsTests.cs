using System.IO;
using NUnit.Framework;

/// <summary>
/// Fit-to-volume must not divide by zero on empty/degenerate mesh bounds.
/// </summary>
public sealed class SpzGoFitVolumeDegenerateBoundsTests {

	[Test]
	public void RescaleModel_GuardsEmptyMaxDimension() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "Objs3D_Container.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("maxDimension < 1e-8f"),
			"RescaleModel_fitIntoVolume must refuse divide-by-zero on degenerate bounds.");
		Assert.That(src, Does.Contain("fit-to-volume skipped"),
			"Degenerate path should log so GO scale bugs are diagnosable.");
	}
}
