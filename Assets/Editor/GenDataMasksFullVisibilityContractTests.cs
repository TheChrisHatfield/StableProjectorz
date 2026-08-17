using System.IO;
using NUnit.Framework;

/// <summary>
/// GenData_Masks visibility init must cover every UV wrap kind that should start fully visible.
/// A copy-pasted duplicate left UvPaintedBrush on Color.clear, so bake-colors / UV brush layers
/// looked invisible until something else repainted visibility.
/// </summary>
public sealed class GenDataMasksFullVisibilityContractTests {

	[Test]
	public void FullVisibility_IncludesUvPaintedBrush_NotADuplicateNormalsLine() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "GenData", "GenData_Masks.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("bool full_visibility", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int j = src.IndexOf("Color visibilityCol", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("UvPaintedBrush"),
			"UV painted brush layers must start fully visible like file UV textures");
		Assert.That(body, Does.Contain("UvTextures_FromFile"));
		Assert.That(body, Does.Contain("UvNormals_FromFile"));
		int firstNormals = body.IndexOf("UvNormals_FromFile", System.StringComparison.Ordinal);
		int secondNormals = body.IndexOf("UvNormals_FromFile", firstNormals + 1, System.StringComparison.Ordinal);
		Assert.That(secondNormals, Is.LessThan(0),
			"duplicate UvNormals_FromFile was a copy-paste that dropped UvPaintedBrush");
	}
}
