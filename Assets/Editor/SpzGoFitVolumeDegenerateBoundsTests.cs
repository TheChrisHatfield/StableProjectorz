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
		// Reset before early-outs so empty roots cannot keep a previous model's fit factor.
		int resetAt = src.IndexOf("currModelRoot_scaleAfterImport = 1f;");
		int earlyReturn = src.IndexOf("if(renderer.Length == 0){ return; }");
		Assert.That(resetAt, Is.GreaterThan(0));
		Assert.That(earlyReturn, Is.GreaterThan(resetAt),
			"Fit scale must reset to 1 before the no-renderer early return.");
	}
}
