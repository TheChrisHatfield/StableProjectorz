using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Temp-RT / busy-chrome cleanup for dilation, depth blur, and Gen3D_MGR teardown.
/// </summary>
public sealed class DilateBlurGen3dTeardownContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing: {path}");
		return File.ReadAllText(path);
	}

	[Test]
	public void DilateCrtnCleansUpInFinally() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "Dilation", "TextureDilation_MGR.cs");
		int i = src.IndexOf("IEnumerator dilate_crtn(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void CheckAsserts(", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("} finally {"));
		Assert.That(body, Does.Contain("Cleanup();"));
		Assert.That(body.IndexOf("Cleanup();", StringComparison.Ordinal),
			Is.GreaterThan(body.IndexOf("} finally {", StringComparison.Ordinal)));
	}

	[Test]
	public void DepthBlurReleasesHelperInFinally() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections", "Depth_Contrast_Helper.cs");
		int i = src.IndexOf("void Blur_Depth_maybe(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void Awake()", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("} finally {"));
		Assert.That(body, Does.Contain("RenderTexture.ReleaseTemporary(helper);"));
	}

	[Test]
	public void Gen3dMgrDestroyClearsPendingImportAndCancelChrome() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		int i = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i);
		Assert.That(body, Does.Contain("ClearPendingMeshImportFinish();"));
		Assert.That(body, Does.Contain("OnConfirmed_FinishedGenerate(canceled: true)"),
			"destroy mid import-wait must not leave Generate stuck in Cancel");
	}
}
