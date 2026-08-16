using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Rembg and Shadow R hold the shared SD generation gate while their external process runs
/// (SubmitCustomWorkflow + OnConfirmed_StartedGenerate). Their coroutines are owned by the component,
/// so destroying it mid-run stops the coroutine and its MarkCustomWorkflow_Done never executes.
/// The hub then stays in rembg_backgroundRemoval / Shadow_R_delighting: Generate is stuck showing
/// Cancel and StableDiffusion_Hub refuses every later generation until restart.
/// </summary>
public sealed class PythonRunnerDestroyReleasesGenGateContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing source: {path}");
		return File.ReadAllText(path);
	}

	static void AssertDestroyReleasesGate(string src, string runnerName, string workflowEnum) {
		int destroy = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(destroy, Is.GreaterThan(0), $"{runnerName}: OnDestroy must exist");
		string destroyBody = src.Substring(destroy, Math.Min(420, src.Length - destroy));
		Assert.That(destroyBody, Does.Contain("ReleaseGenerationGate_ifMine();"),
			$"{runnerName}: destroy must release the SD gate, not only kill the process");

		int release = src.IndexOf("void ReleaseGenerationGate_ifMine()", StringComparison.Ordinal);
		Assert.That(release, Is.GreaterThan(0), $"{runnerName}: release helper must exist");
		string body = src.Substring(release, Math.Min(600, src.Length - release));

		Assert.That(body, Does.Contain("StableDiffusion_Hub.instance"));
		Assert.That(body, Does.Contain("hub == null"),
			$"{runnerName}: teardown order can null the hub first");
		Assert.That(body, Does.Contain(workflowEnum),
			$"{runnerName}: only release the gate when this runner owns the in-flight workflow");
		Assert.That(body, Does.Contain("MarkCustomWorkflow_Done();"),
			$"{runnerName}: the hub must be returned to 'nothing' or all later generation is refused");
		Assert.That(body, Does.Contain("OnConfirmed_FinishedGenerate(canceled"),
			$"{runnerName}: the Generate button must leave Cancel mode");
	}

	[Test]
	public void RembgDestroyReleasesTheGate() {
		string src = Read("Assets", "_gm", "Features", "Paint", "BackgroundRemoval",
			"Rembg_PythonRunner.cs");
		AssertDestroyReleasesGate(src, "Rembg",
			"Generate_RequestingWhat.rembg_backgroundRemoval");
	}

	[Test]
	public void ShadowRDestroyReleasesTheGate() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "Delight",
			"ShadowR_PythonRunner.cs");
		AssertDestroyReleasesGate(src, "ShadowR",
			"Generate_RequestingWhat.Shadow_R_delighting");
	}

	[Test]
	public void ReleaseMatchesTheProvenCancelOrdering() {
		// The cancel button already does the right thing; destroy must not invent a different order.
		foreach (var parts in new[] {
			new[] { "Assets", "_gm", "Features", "Paint", "BackgroundRemoval", "Rembg_PythonRunner.cs" },
			new[] { "Assets", "_gm", "Features", "TextureTools", "Delight", "ShadowR_PythonRunner.cs" },
		}) {
			string src = Read(parts);
			int release = src.IndexOf("void ReleaseGenerationGate_ifMine()", StringComparison.Ordinal);
			string body = src.Substring(release, Math.Min(600, src.Length - release));
			int finished = body.IndexOf("OnConfirmed_FinishedGenerate(", StringComparison.Ordinal);
			int marked = body.IndexOf("MarkCustomWorkflow_Done(", StringComparison.Ordinal);
			Assert.That(finished, Is.GreaterThan(0));
			Assert.That(marked, Is.GreaterThan(finished),
				"UI leaves Cancel first, then the hub is marked done — same as OnCancel*_Button");
		}
	}
}
