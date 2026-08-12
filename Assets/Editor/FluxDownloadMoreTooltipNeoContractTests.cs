using System.IO;
using NUnit.Framework;

/// <summary>
/// Flux download-more tooltips must match Forge Neo + Klein structure docs
/// (ImageStitch / RefControl — not "wait for Illyasviel ControlNet").
/// </summary>
public sealed class FluxDownloadMoreTooltipNeoContractTests {

	static string RepoPath(params string[] parts) {
		return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));
	}

	[Test]
	public void ModelDownloadMore_FluxTooltipsMatchNeoDocs() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel",
			"Download More NeuralNets (Slide widget).prefab");
		AssertTooltip(File.ReadAllText(path));
	}

	[Test]
	public void ControlNetDownloadMore_FluxTooltipMatchesNeoDocs() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"Controlnet Download More NeuralNets (Slide widget).prefab");
		AssertTooltip(File.ReadAllText(path));
	}

	static void AssertTooltip(string src) {
		Assert.That(src, Does.Contain("ImageStitch"));
		Assert.That(src, Does.Contain("RefControl"));
		Assert.That(src, Does.Contain("Forge Neo"));
		Assert.That(src, Does.Contain("SD1.5/XL ControlNet still works"));
		Assert.That(src, Does.Not.Contain("Illyasviel"),
			"Outdated wait-for-classic-Forge copy must not remain.");
		Assert.That(src, Does.Not.Contain("doesn''t work yet with Forge Webui"),
			"Outdated Flux-CN-unsupported-on-Forge copy must not remain.");
	}
}
