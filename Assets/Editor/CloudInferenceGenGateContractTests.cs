using System.IO;
using NUnit.Framework;

/// <summary>
/// Cloud Inference (fal/Demo shim) has stub CN catalogs — Hub must not block Gen Art on missing Depth.
/// </summary>
public sealed class CloudInferenceGenGateContractTests {

	[Test]
	public void Hub_SourceSkipsControlNetGateWhenCloudInference() {
		string hub = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs");
		string mgr = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Connection", "Connection_MGR.cs");
		Assert.That(File.Exists(hub), Is.True);
		Assert.That(File.Exists(mgr), Is.True);
		string hubSrc = File.ReadAllText(hub);
		string mgrSrc = File.ReadAllText(mgr);
		Assert.That(mgrSrc, Does.Contain("is_cloud_inference"));
		Assert.That(hubSrc, Does.Contain("Connection_MGR.is_cloud_inference"));
		Assert.That(hubSrc, Does.Contain("is_cloud_inference"),
			"Gen Art / Deny paths must consult cloud inference so fal Connect is not a dead end.");
	}
}
