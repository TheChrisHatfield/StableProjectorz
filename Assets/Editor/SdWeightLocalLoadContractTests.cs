using System.IO;
using NUnit.Framework;

/// <summary>
/// sd-weight-local-load: browse / drag-drop copy into WebUI checkpoint &amp; VAE dirs.
/// Hook <c>sd.weight_local_load</c>.
/// </summary>
public sealed class SdWeightLocalLoadContractTests {

	static string RepoPath(params string[] parts) {
		return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));
	}

	[Test]
	public void SysInfo_ResolveCheckpointAndVaeDirs() {
		string path = RepoPath("Assets", "_gm", "Features", "StableDiffusion", "SD_SysInfo_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryResolveCheckpointModelsDir"));
		Assert.That(src, Does.Contain("TryResolveVaeModelsDir"));
		Assert.That(src, Does.Contain("/models/Stable-diffusion/"));
		Assert.That(src, Does.Contain("/models/VAE/"));
		Assert.That(src, Does.Contain("TryGetSdDataPath"));
	}

	[Test]
	public void Importer_GatesOnDataPathAndExtensions() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_WeightFileImport.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryResolveDestDir"));
		Assert.That(src, Does.Contain("TryResolveCheckpointModelsDir"));
		Assert.That(src, Does.Contain("TryResolveVaeModelsDir"));
		Assert.That(src, Does.Contain("IsWeightExtension"));
		Assert.That(src, Does.Contain(".safetensors"));
		Assert.That(src, Does.Contain("FileBrowser.ShowLoadDialog"));
		Assert.That(src, Does.Contain("File.Copy"));
		Assert.That(src, Does.Contain("PreferModelWhenAvailable"));
		Assert.That(src, Does.Contain("PreferVAEWhenAvailable"));
		Assert.That(src, Does.Contain("EnsureFromDiskButton"));
		Assert.That(src, Does.Contain("From disk"));
		Assert.That(src, Does.Contain("DataPath empty").Or.Contain("denyReason"));
	}

	[Test]
	public void NeuralModels_WiresBrowsePreferAndHitTest() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Neural_Models.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("BrowseAndImportCheckpoint"));
		Assert.That(src, Does.Contain("PreferModelWhenAvailable"));
		Assert.That(src, Does.Contain("ScreenPointHitsOwnership"));
		Assert.That(src, Does.Contain("EnsureLoadFromDiskButton"));
		Assert.That(src, Does.Contain("SD_WeightFileImport"));
		Assert.That(src, Does.Contain("OrdinalIgnoreCase"));
		Assert.That(src, Does.Not.Contain("opt.text.IndexOf(want"),
			"PreferModel must not fuzzy IndexOf — short stems false-positive longer checkpoint names.");
	}

	[Test]
	public void Vae_WiresBrowsePreferAndHitTest() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_VAE.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("BrowseAndImportVAE"));
		Assert.That(src, Does.Contain("PreferVAEWhenAvailable"));
		Assert.That(src, Does.Contain("ScreenPointHitsOwnership"));
		Assert.That(src, Does.Contain("EnsureLoadFromDiskButton"));
		Assert.That(src, Does.Contain("SD_WeightFileImport"));
	}

	[Test]
	public void DragAndDrop_RoutesWeightsWithHitTest() {
		string path = RepoPath("Assets", "UnityWindowsFileDrag-Drop", "FileDragAndDrop.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("AllFilesAreWeights"));
		Assert.That(src, Does.Contain("ScreenPointHitsOwnership"));
		Assert.That(src, Does.Contain("Kind.Checkpoint"));
		Assert.That(src, Does.Contain("Kind.Vae"));
		Assert.That(src, Does.Contain("Drop onto Model or SD-VAE"));
	}
}
