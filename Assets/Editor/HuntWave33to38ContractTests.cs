using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntWave33to38ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void HdrSphereResult_AlwaysMarksCustomWorkflowDone() {
		string src = Read("Assets", "_gm", "Features", "Skybox + Background", "HDR_PanoSkybox_MGR.cs");
		int i = src.IndexOf("void OnGeneratedSpheres_Result(", StringComparison.Ordinal);
		int end = src.IndexOf("SD_ControlnetDetect_payload make_ctrlnetDetect_payload", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("MarkCustomWorkflow_Done"));
		Assert.That(body, Does.Contain("response == null"));
		Assert.That(src, Does.Contain("progressResponse.state == null"));
	}

	[Test]
	public void ControlNetThumbs_ClearsClickedOnDeadUnit() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlNetUnits_ThumbsList_UI.cs");
		Assert.That(src, Does.Contain("ReferenceEquals(_clickedThumb, thumb)"));
		Assert.That(src, Does.Contain("_clickedThumb._myUnit == null"));
	}

	[Test]
	public void SelectProjCamera_GuardsNullIcon() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections", "ProjectorCameras_MGR.cs");
		int i = src.IndexOf("void Select_Specific_ProjCamera(", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(450, src.Length - i));
		Assert.That(body, Does.Contain("icon == null || icon._genData == null"));
	}

	[Test]
	public void Gen3dImageInput_UnsubscribesScreenshotAllow() {
		string src = Read("Assets", "_gm", "Features", "3D Generate",
			"Gen3D_InputPanelBuilder_UI", "Gen3D_Single_ImageInput_UI.cs");
		Assert.That(src, Does.Contain("Act_AllowTakeScreenshots -= OnAllowTakeScreenshots"));
		int dis = src.IndexOf("protected virtual void OnDisable()", StringComparison.Ordinal);
		string dbody = src.Substring(dis, Math.Min(400, src.Length - dis));
		Assert.That(dbody, Does.Contain("Screenshot_MGR.instance != null"));
	}

	[Test]
	public void SkyboxMgr_UnsubscribesGuidChangesOnDestroy() {
		string src = Read("Assets", "_gm", "Features", "Icons", "SkyboxBackground_MGR.cs");
		Assert.That(src, Does.Contain("Act_OnSomeIcon_TextureGuidsChanged -= OnSomeIcon_TextureGuidsChanged"));
		int forget = src.IndexOf("void ForgetCurrentIconUI_ifCan(", StringComparison.Ordinal);
		string fbody = src.Substring(forget, Math.Min(500, src.Length - forget));
		Assert.That(fbody.IndexOf("Act_OnSomeBgBlends_sliders -=", StringComparison.Ordinal),
			Is.LessThan(fbody.IndexOf("_genData == null", StringComparison.Ordinal)));
	}

	[Test]
	public void VisualizeFinalMat_NullGuardsSelectAndHub() {
		string src = Read("Assets", "_gm", "Features", "Render", "VisualizeFinalMat_Helper.cs");
		Assert.That(src, Does.Contain("ClickSelect_Meshes_MGR.instance != null"));
		Assert.That(src, Does.Contain("StableDiffusion_Hub.instance == null"));
		Assert.That(src, Does.Contain("ModelsHandler_3D.instance == null) return"));
	}
}
