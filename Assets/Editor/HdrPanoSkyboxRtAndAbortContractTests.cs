using System.IO;
using NUnit.Framework;

/// <summary>
/// HDR panoramic allocates sphere RTs and runs a dark/light loop. Cancel/error must stop further
/// SD submits, and RTs must be destroyed (previously only combinedDepth was freed).
/// </summary>
public sealed class HdrPanoSkyboxRtAndAbortContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Skybox + Background", "HDR_PanoSkybox_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void SphereLoop_AbortsOnErrorAndReleasesAllRts() {
		string src = ReadSrc();
		int i = src.IndexOf("IEnumerator Generate_PanoramicHDR_crtn(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int next = src.IndexOf("IEnumerator GuessDepthFromArt_crtn(", i + 1, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(i));
		string body = src.Substring(i, next - i);

		Assert.That(body, Does.Contain("if (_guessedDepth == null)"),
			"depth-detect yield-break must not continue into Combine/SD");
		Assert.That(body, Does.Contain("if (_sphereGen_error) yield break"),
			"cancel/error must stop further dark/light sphere submits");
		Assert.That(body, Does.Contain("finally"),
			"sphere RTs must be freed on success, cancel, and error");
		Assert.That(body, Does.Contain("DestroyImmediate(depthWithSphere)"),
			"depthWithSphere was previously leaked");
		Assert.That(body, Does.Contain("DestroyImmediate(maskWithSphere)"),
			"maskWithSphere was previously leaked");
	}
}
