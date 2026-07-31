using System.IO;
using NUnit.Framework;

/// <summary>
/// Cancel must KillProcessTree the rembg/ShadowR CMD — StopAllCoroutines alone left Python writing VRAM.
/// </summary>
public sealed class RembgShadowRCancelKillContractTests {

	[Test]
	public void Rembg_CancelKillsActiveProcessTree() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BackgroundRemoval", "Rembg_PythonRunner.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_activeProcessId"));
		Assert.That(src, Does.Contain("KillActiveRembgProcess"));
		Assert.That(src, Does.Contain("KillProcessTree"));
		int cancel = src.IndexOf("void OnCancelRembg_Button()", System.StringComparison.Ordinal);
		int stop = src.IndexOf("StopAllCoroutines()", cancel, System.StringComparison.Ordinal);
		Assert.That(src.IndexOf("KillActiveRembgProcess()", cancel, System.StringComparison.Ordinal),
			Is.GreaterThan(0).And.LessThan(stop),
			"Must kill the process tree before StopAllCoroutines.");
	}

	[Test]
	public void ShadowR_CancelKillsActiveProcessTree() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "Delight", "ShadowR_PythonRunner.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("KillActiveShadowRProcess"));
		Assert.That(src, Does.Contain("KillProcessTree"));
		int cancel = src.IndexOf("void OnCancelShadowR_Button()", System.StringComparison.Ordinal);
		int stop = src.IndexOf("StopAllCoroutines()", cancel, System.StringComparison.Ordinal);
		Assert.That(src.IndexOf("KillActiveShadowRProcess()", cancel, System.StringComparison.Ordinal),
			Is.GreaterThan(0).And.LessThan(stop),
			"Must kill the process tree before StopAllCoroutines.");
	}
}
