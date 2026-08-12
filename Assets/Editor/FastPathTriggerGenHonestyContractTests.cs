using System.IO;
using NUnit.Framework;

/// <summary>
/// FastPath TriggerTextureGeneration must not report success when Hub.Generate was denied.
/// </summary>
public sealed class FastPathTriggerGenHonestyContractTests {

	[Test]
	public void TriggerTextureGeneration_ReturnsTrueOnlyIfGeneratingOrPreparing() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int start = src.IndexOf("public bool TriggerTextureGeneration(", System.StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("public bool StopGeneration(", start, System.StringComparison.Ordinal);
		string body = src.Substring(start, end - start);
		Assert.That(body, Does.Contain("sdHub.Generate("));
		Assert.That(body, Does.Contain("return sdHub._generating || sdHub._finalPreparations_beforeGen"));
		Assert.That(body, Does.Not.Contain("return true;"));
	}
}
