using System.IO;
using NUnit.Framework;

/// <summary>
/// Checkpoint / VAE panels subscribe to SD_Options_Fetcher static buses in Start. Without OnDestroy
/// leave, reload keeps stale handlers mutating destroyed dropdowns / ammend options packets.
/// </summary>
public sealed class SdOptionsFetcherLeaveContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	static void AssertOptionsLeave(string src, string typeHint) {
		Assert.That(src, Does.Contain("Act_onOptionsRetrieved += OnOptionsReceived"), typeHint);
		Assert.That(src, Does.Contain("void OnDestroy()"), typeHint + " must leave the options bus");
		Assert.That(src, Does.Contain("Act_onOptionsRetrieved -= OnOptionsReceived"), typeHint);
		Assert.That(src, Does.Contain("Act_onWillSendOptions_AmmendPlz -= OnWillSendOptions_AmmendPlz"), typeHint);
		Assert.That(src, Does.Contain("Act_OnSendOptions_done -= OnSendOptions_done"), typeHint);
		Assert.That(src, Does.Contain("instance = null"), typeHint);
	}

	[Test]
	public void NeuralModelsAndVae_UnsubscribeOptionsFetcherOnDestroy() {
		AssertOptionsLeave(
			Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Neural_Models.cs"),
			"SD_Neural_Models");
		AssertOptionsLeave(
			Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_VAE.cs"),
			"SD_VAE");
	}
}
