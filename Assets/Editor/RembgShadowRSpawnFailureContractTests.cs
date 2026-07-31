using System.IO;
using NUnit.Framework;

/// <summary>
/// Rembg/ShadowR outer workflows must not mark generate finished successfully when spawn fails.
/// Inner RunCommand yield break alone left canceled:false + onReady/Get_OutputTextures running.
/// </summary>
public sealed class RembgShadowRSpawnFailureContractTests {

	[Test]
	public void Rembg_DoesNotFinishSuccessWhenSpawnFails() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BackgroundRemoval", "Rembg_PythonRunner.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("reportOk"),
			"RunCommand must report spawn success/failure to the outer workflow.");
		Assert.That(src, Does.Contain("if (!ranOk)"),
			"Outer Rembg_crtn must branch on spawn failure.");
		Assert.That(src, Does.Contain("OnConfirmed_FinishedGenerate(canceled: true)"),
			"Spawn failure must finish as canceled, not success.");
		int fail = src.IndexOf("if (!ranOk)", System.StringComparison.Ordinal);
		int success = src.IndexOf("OnConfirmed_FinishedGenerate(canceled: false)", fail, System.StringComparison.Ordinal);
		Assert.That(success, Is.GreaterThan(fail),
			"Success finish must only run after the ranOk gate.");
	}

	[Test]
	public void ShadowR_DoesNotFinishSuccessWhenSpawnFails() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "Delight", "ShadowR_PythonRunner.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("if (!ranOk)"));
		Assert.That(src, Does.Contain("OnConfirmed_FinishedGenerate(canceled:true)"));
		int fail = src.IndexOf("if (!ranOk)", System.StringComparison.Ordinal);
		int getOut = src.IndexOf("Get_OutputTextures_from_Dir", fail, System.StringComparison.Ordinal);
		Assert.That(getOut, Is.GreaterThan(fail),
			"Must not register output GenData after a failed spawn.");
		string failBlock = src.Substring(fail, getOut - fail);
		Assert.That(failBlock, Does.Contain("yield break"),
			"Spawn failure must exit before Get_OutputTextures_from_Dir.");
	}
}
