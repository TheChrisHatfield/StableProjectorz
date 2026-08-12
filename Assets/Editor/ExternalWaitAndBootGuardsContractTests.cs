using System.IO;
using NUnit.Framework;

public sealed class WaitForRenderAllAlwaysCompletesContractTests {
	[Test]
	public void WaitForRenderAll_InvokesOnReadyInFinally() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator WaitForRenderAll_crtn(", System.StringComparison.Ordinal);
		int j = src.IndexOf("void Save_ViewTextures(", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("onReady?.Invoke()"));
		Assert.That(body, Does.Contain("Objects_Renderer_MGR.instance != null"));
	}
}

public sealed class SdVaeCoroutinesMgrGuardContractTests {
	[Test]
	public void Start_DefersUntilCoroutinesMgrReady() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_VAE.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureFetchLoopStarted_crtn"));
		Assert.That(src, Does.Contain("while (Coroutines_MGR.instance == null)"));
	}
}

public sealed class ExternalProcessWaitTimeoutContractTests {
	[Test]
	public void ShadowR_And_Rembg_PollHaveTimeoutAndProcessExit() {
		string shadow = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "Delight", "ShadowR_PythonRunner.cs");
		string rembg = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BackgroundRemoval", "Rembg_PythonRunner.cs");
		foreach (string path in new[] { shadow, rembg }) {
			string src = File.ReadAllText(path);
			Assert.That(src, Does.Contain("maxWaitSec"), path);
			Assert.That(src, Does.Contain("IsProcessRunning(processId)"), path);
			Assert.That(src, Does.Contain("reportOk?.Invoke(false)"), path);
		}
	}
}

public sealed class FastPathSkyboxClearHonestyContractTests {
	[Test]
	public void IsSkyboxGradientClear_FalseWhenUnknown() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool IsSkyboxGradientClear()", System.StringComparison.Ordinal);
		int j = src.IndexOf("public Color? GetSkyboxTopColor()", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("if (!_isInitialized) return false;"));
		Assert.That(body, Does.Contain("if (skybox == null) return false;"));
	}
}
