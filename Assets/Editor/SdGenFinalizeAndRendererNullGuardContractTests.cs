using System.IO;
using NUnit.Framework;

/// <summary>
/// Gen finalize / prep must not NRE on missing ControlNet list or Objects_Renderer_MGR.
/// </summary>
public sealed class SdGenFinalizeAndRendererNullGuardContractTests {

	[Test]
	public void Finalize_GuardsXlAdviceAndClearsPrepFlags() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Finalize_GenerationRequest(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("try"));
		Assert.That(body, Does.Contain("_finalPreparations_beforeGen = false"));
		Assert.That(body, Does.Contain("apppend_sdxl_ctrlnet_advice_maybe"));
	}

	[Test]
	public void XlCtrlNetAdvice_NullGuardsListAndInputPanel() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void apppend_sdxl_ctrlnet_advice_maybe", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("SD_ControlNetsList_UI.instance == null"));
		Assert.That(body, Does.Contain("SD_InputPanel_UI.instance == null"));
	}

	[Test]
	public void GenPrep_UsesNullConditionalReRenderAllSoon() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Objects_Renderer_MGR.instance?.ReRenderAll_soon()"));
		Assert.That(src, Does.Not.Contain("Objects_Renderer_MGR.instance.ReRenderAll_soon()"),
			"unguarded instance.ReRenderAll_soon can stick generating flags");
	}

	[Test]
	public void ApplyInpaintSketch_NullGuardsMainViewportInstance() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Render", "Objects_Renderer_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Apply_InpaintSketch_ColorLayer()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(400, src.Length - i));
		Assert.That(body, Does.Contain("MainViewport_UI.instance == null"),
			"missing viewport singleton must not NRE mid-render");
	}
}
