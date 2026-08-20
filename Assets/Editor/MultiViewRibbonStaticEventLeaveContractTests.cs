using System.IO;
using NUnit.Framework;

/// <summary>
/// MultiView ribbon subscribed to WorkflowRibbon mode + GenData archive static buses with an
/// anonymous mode lambda (cannot -=) and no OnDestroy leave → dead handlers after reload.
/// </summary>
public sealed class MultiViewRibbonStaticEventLeaveContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void ModeAndGenDataBuses_UseNamedHandlersAndOnDestroyLeave() {
		string src = ReadSrc();
		Assert.That(src, Does.Contain("_Act_OnModeChanged += OnWorkflowMode_Changed"));
		Assert.That(src, Does.Not.Contain("_Act_OnModeChanged += ("),
			"anonymous mode lambda cannot be removed on leave");
		Assert.That(src, Does.Contain("OnWillDispose_GenerationData += OnWillDispose_GenerationData"));
		Assert.That(src, Does.Contain("OnWillGenerate += On_SD_willGenerateArt"));

		Assert.That(src, Does.Contain("_Act_OnModeChanged -= OnWorkflowMode_Changed"));
		Assert.That(src, Does.Contain("OnWillDispose_GenerationData -= OnWillDispose_GenerationData"));
		Assert.That(src, Does.Contain("OnWillGenerate -= On_SD_willGenerateArt"));
		Assert.That(src, Does.Contain("instance = null"));
	}
}
