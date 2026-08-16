using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Resource cleanup: AO depth temp RT on destroy, and UnityWebRequest dispose on options/version paths.
/// </summary>
public sealed class ResourceDisposeOnTeardownContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing: {path}");
		return File.ReadAllText(path);
	}

	[Test]
	public void AoBakerDestroyReleasesDepthTemporary() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "AO", "AmbientOcclusion_Baker.cs");
		int i = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("Dispose_RT(ref _depth_tempTargTexture, isTemporary:true)"),
			"mid-bake destroy must return the GetTemporary depth RT to the pool");
	}

	[Test]
	public void OptionsFetcherDisposesWebRequests() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Options_Fetcher.cs");
		Assert.That(src, Does.Contain("using (UnityWebRequest request = UnityWebRequest.Get("),
			"FetchOptions polls often — native HTTP handles must not accumulate");
		Assert.That(src, Does.Contain("using (UnityWebRequest request = new UnityWebRequest("),
			"SendOptionsRequest must dispose too");
	}

	[Test]
	public void VersionCheckDisposesBothRequestsInFinally() {
		string src = Read("Assets", "_gm", "Features", "Intro Panels", "Version Popup UI",
			"CheckForUpdates_MGR.cs");
		int i = src.IndexOf("IEnumerator checkVersions_crtn()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("bool ProcessWebResponse(", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i), "anchor on the real method block");
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("} finally {"));
		Assert.That(body, Does.Contain("pastebinWWW?.Dispose();"));
		Assert.That(body, Does.Contain("websiteWWW?.Dispose();"));
	}
}
