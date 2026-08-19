using System.IO;
using NUnit.Framework;

public sealed class HuntCampaign2Wave5ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void Upscalers_FetchBusyClearedInFinally() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel",
			"SD_Upscalers.cs");
		Assert.That(src, Does.Contain("hub == null || hub._generating"));
		Assert.That(src, Does.Contain("finally"));
		Assert.That(src, Does.Contain("_isFetchingUpscalers = false"));
	}

	[Test]
	public void SdHub_NullGuardsDimsAndUnsubscribesExport() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion",
			"StableDiffusion_Hub.cs");
		Assert.That(src, Does.Contain("if (DimensionMode_MGR.instance == null) return"));
		Assert.That(src, Does.Contain("OnExportFinalTex_Button -= OnExportFinalTex_DilateTrue"));
		Assert.That(src, Does.Contain("OnExportViews_Button -= OnExportViewTextures_Button"));
	}

	[Test]
	public void Screenshot_NullGuardsViewport() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "Screenshot",
			"Screenshot_MGR.cs");
		Assert.That(src, Does.Contain("MainViewport_UI.instance == null || !MainViewport_UI.instance.isCursorHoveringMe()"));
		Assert.That(src, Does.Contain("innerViewportRect == null) return"));
	}

	[Test]
	public void InpaintDummyText_NullGuardsUpdate() {
		string src = Read("Assets", "_gm", "Features", "Paint", "Inpaint",
			"Inpaint_DummyTextMaker.cs");
		Assert.That(src, Does.Contain("WorkflowRibbon_UI.instance == null"));
		Assert.That(src, Does.Contain("SD_WorkflowOptionsRibbon_UI.instance == null"));
	}

	[Test]
	public void SkyboxRect_NullGuardsContentCamChain() {
		string src = Read("Assets", "_gm", "Features", "Icons",
			"SkyboxBackground_Rect_UI.cs");
		Assert.That(src, Does.Contain("view != null ? view.contentCam"));
		Assert.That(src, Does.Not.Contain("instance?._curr_viewCamera.contentCam"));
	}
}
