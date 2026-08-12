using System.IO;
using NUnit.Framework;

public sealed class AddonBlInfoMetadataParseContractTests {

	[Test]
	public void TryParseInitPyMetadata_ReadsBlInfoVersionTupleAndDescription() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("static void TryParseInitPyMetadata(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(2200, src.Length - i));
		Assert.That(body, Does.Contain(@"[""']version[""']\s*:\s*\("),
			"Must parse bl_info version tuples like (1, 0, 0).");
		Assert.That(body, Does.Contain(@"[""']description[""']\s*:"),
			"bl_info description must fill Summary when addon.json is missing.");
	}
}
