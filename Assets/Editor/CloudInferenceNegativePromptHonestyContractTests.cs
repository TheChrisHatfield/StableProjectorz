using System.IO;
using NUnit.Framework;

/// <summary>
/// fal FLUX drops negatives — Unity must notice info.negative_prompt_ignored when cloud-connected.
/// </summary>
public sealed class CloudInferenceNegativePromptHonestyContractTests {

	[Test]
	public void GenHelper_SourceSurfacesIgnoredNegativeWhenCloud() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("negative_prompt_ignored"));
		Assert.That(src, Does.Contain("Connection_MGR.is_cloud_inference"));
		Assert.That(src, Does.Contain("negative prompt was ignored"));
	}
}
