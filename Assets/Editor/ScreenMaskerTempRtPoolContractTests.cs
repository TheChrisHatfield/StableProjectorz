using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Screen-mask helper RTs come from RenderTexture.GetTemporary, so they must be returned with
/// ReleaseTemporary. Release() frees the surface but leaves the RT checked out of Unity's temporary
/// pool forever, so every paint/SD screen-mask rebuild allocates another one — steady VRAM growth.
/// The repo's own TextureTools_SPZ.Dispose_RT documents the correct API for temporaries.
/// </summary>
public sealed class ScreenMaskerTempRtPoolContractTests {

	static readonly string[][] MaskerFiles = {
		new[] { "Assets", "_gm", "Features", "Paint", "Inpaint", "Inpaint_ScreenMasker_Original.cs" },
		new[] { "Assets", "_gm", "Features", "Paint", "Inpaint", "Inpaint_ScreenMasker_EmptyNothing.cs" },
	};

	static string Read(string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing source: {path}");
		return File.ReadAllText(path);
	}

	[Test]
	public void HelperDisposeReturnsTemporariesToThePool() {
		foreach (var parts in MaskerFiles) {
			string src = Read(parts);
			string name = parts[parts.Length - 1];

			int dispose = src.IndexOf("public void Dispose()", StringComparison.Ordinal);
			Assert.That(dispose, Is.GreaterThan(0), $"{name}: helper Dispose must exist");
			string body = src.Substring(dispose, Math.Min(900, src.Length - dispose));

			Assert.That(body, Does.Contain("ReturnToPool(mask)"), $"{name}: mask must be pooled back");
			Assert.That(body, Does.Contain("ReturnToPool(edges)"), $"{name}: edges must be pooled back");
			Assert.That(body, Does.Contain("ReturnToPool(edgesBuffer)"),
				$"{name}: edgesBuffer must be pooled back");

			Assert.That(body, Does.Not.Contain("mask?.Release()"),
				$"{name}: Release() never returns a GetTemporary RT to the pool");
			Assert.That(body, Does.Not.Contain("edges?.Release()"), $"{name}: same for edges");
			Assert.That(body, Does.Not.Contain("edgesBuffer?.Release()"),
				$"{name}: same for edgesBuffer");
		}
	}

	[Test]
	public void ReturnToPoolUsesReleaseTemporaryAndClearsActive() {
		foreach (var parts in MaskerFiles) {
			string src = Read(parts);
			string name = parts[parts.Length - 1];

			int helper = src.IndexOf("static void ReturnToPool(RenderTexture rt)", StringComparison.Ordinal);
			Assert.That(helper, Is.GreaterThan(0), $"{name}: pooling helper must exist");
			string body = src.Substring(helper, Math.Min(320, src.Length - helper));

			Assert.That(body, Does.Contain("RenderTexture.ReleaseTemporary(rt);"),
				$"{name}: temporaries must be released with ReleaseTemporary");
			Assert.That(body, Does.Contain("RenderTexture.active == rt"),
				$"{name}: recycling the active RT would send later renders to the wrong target");
		}
	}

	[Test]
	public void RepoHelperStillDocumentsTheTemporaryApi() {
		// Guards the precedent this fix is based on.
		string src = Read(new[] { "Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs" });
		Assert.That(src, Does.Contain("Dispose_RT(ref RenderTexture rt, bool isTemporary)"));
		Assert.That(src, Does.Contain("if(isTemporary){ RenderTexture.ReleaseTemporary(rt);"));
	}
}
