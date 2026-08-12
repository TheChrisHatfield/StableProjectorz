using System.IO;
using NUnit.Framework;

/// <summary>
/// Flux download-more tooltips must match Forge Neo + Klein structure docs
/// (ImageStitch / RefControl — not "wait for Illyasviel ControlNet").
/// Model slide-out = checkpoint guidance; ControlNet slide-out = Fun-Union download.
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
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Flux.1 base checkpoint"));
		Assert.That(src, Does.Contain("ControlNet Download More"));
		Assert.That(src, Does.Contain("ImageStitch"));
		Assert.That(src, Does.Contain("RefControl"));
		Assert.That(src, Does.Contain("Klein"));
		Assert.That(src, Does.Not.Contain("download Fun-Union into models/ControlNet"),
			"Model slide-out must not claim the Flux.1 button downloads Fun-Union CN.");
		AssertNoOutdatedWaitCopy(src);
	}

	[Test]
	public void ControlNetDownloadMore_FluxTooltipMatchesNeoDocs() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"Controlnet Download More NeuralNets (Slide widget).prefab");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("alibaba-pai/FLUX.2-dev-Fun-Controlnet-Union"));
		Assert.That(src, Does.Contain("Fun-Union (FLUX.2-dev)"));
		Assert.That(src, Does.Contain("download Fun-Union"));
		Assert.That(src, Does.Contain("FLUX.2-dev"));
		Assert.That(src, Does.Contain("ImageStitch"));
		Assert.That(src, Does.Contain("RefControl"));
		Assert.That(src, Does.Contain("Klein"));
		Assert.That(src, Does.Not.Contain("xlabs-ai"));
		Assert.That(src, Does.Not.Contain("'  m_isRightToLeft"),
			"Prefab YAML must not glue m_isRightToLeft onto m_text.");
		AssertNoOutdatedWaitCopy(src);
	}

	static void AssertNoOutdatedWaitCopy(string src) {
		Assert.That(src, Does.Not.Contain("Illyasviel"),
			"Outdated wait-for-classic-Forge copy must not remain.");
		Assert.That(src, Does.Not.Contain("doesn''t work yet with Forge Webui"),
			"Outdated Flux-CN-unsupported-on-Forge copy must not remain.");
	}
}
