using System.IO;
using NUnit.Framework;

/// <summary>
/// Cloud Inference must not attach Soft Inpaint / ControlNet alwayson (fal 501 / Demo lie).
/// </summary>
public sealed class CloudInferenceSkipUnsupportedAlwaysonContractTests {

	[Test]
	public void PayloadMaker_SourceSkipsSoftInpaintAndControlNetWhenCloud() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("!Connection_MGR.is_cloud_inference"));
		Assert.That(src, Does.Contain("Soft Inpaint skipped on Cloud Inference"));
		Assert.That(src, Does.Contain("!kleinGenArt && !Connection_MGR.is_cloud_inference"));
	}
}
