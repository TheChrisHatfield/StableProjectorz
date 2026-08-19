using System.IO;
using NUnit.Framework;

/// <summary>Hunt campaign wave 4: deferred undo race, unsubscribes, ControlNet/nav null guards.</summary>
public sealed class HuntCampaign2Wave4ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void PaintUndo_FinallyDoesNotWipeArmedFollowUp() {
		string src = Read("Assets", "_gm", "Features", "Paint", "Undo", "PaintUndo_MGR.cs");
		Assert.That(src, Does.Contain("bool armedFollowUp = false"));
		Assert.That(src, Does.Contain("armedFollowUp = _isRestoring"));
		Assert.That(src, Does.Contain("if (!armedFollowUp)"));
	}

	[Test]
	public void WorkflowRibbon_UnsubscribesStrokeAndEarlyUpdate() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "WorkflowRibbon_UI.cs");
		Assert.That(src, Does.Contain("Act_OnPaintStrokeEnd -= OnBrushStrokeEnd"));
		Assert.That(src, Does.Contain("onEarlyUpdate3 -= EarlyUpdate"));
	}

	[Test]
	public void ProjectorCamera_NullGuardsGenDataSaveAndLoad() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections", "ProjectorCamera.cs");
		Assert.That(src, Does.Contain("_myGenData?._masking_utils"));
		Assert.That(src, Does.Contain("if (_myGenData == null || projCamSL == null) return"));
		Assert.That(src, Does.Contain("if (_myGenData == null)"));
	}

	[Test]
	public void ControlNet_PayloadAndImagesNullGuardCams() {
		string unit = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlNetUnit_UI.cs");
		Assert.That(unit, Does.Contain("UserCameras_MGR.instance != null ? UserCameras_MGR.instance.camTextures"));
		Assert.That(unit, Does.Contain("if (trib == null) return null"));
		string imgs = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlNetUnit_ImagesDisplay.cs");
		Assert.That(imgs, Does.Contain("camTextures==null"));
	}

	[Test]
	public void ControlNet_PreprocessorAndThumbNullGuard() {
		string pre = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlnetPreprocessor_UI.cs");
		Assert.That(pre, Does.Contain("SD_InputPanel_UI.instance"));
		Assert.That(pre, Does.Contain("panel != null ? panel.widthHeight()"));
		string thumb = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlNetUnit_Thumb_UI.cs");
		Assert.That(thumb, Does.Contain("LeftRibbon_UI.instance?.SetDepthContrast01_fromCode"));
	}

	[Test]
	public void ControlNetsList_AwakeFallsBackWithoutCoroutinesMgr() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"SD_ControlNetsList_UI.cs");
		Assert.That(src, Does.Contain("StartCtrlNetCrtn"));
		Assert.That(src, Does.Contain("if (crtnMgr != null) return crtnMgr.StartCoroutine(e)"));
	}

	[Test]
	public void ArtBgAndHardness_UnsubscribeOnDestroy() {
		string bg = Read("Assets", "_gm", "Features", "Icons", "IconUI_List_BG",
			"ArtBG_IconsUI_List.cs");
		Assert.That(bg, Does.Contain("OnExportAllArtBG_Icons_Button -="));
		Assert.That(bg, Does.Contain("onImport_BG_fromCurrView -="));
		string hard = Read("Assets", "_gm", "Features", "Paint", "BrushRibbon_UI",
			"BrushRibbon_UI_Hardness.cs");
		Assert.That(hard, Does.Contain("void OnDestroy()"));
		Assert.That(hard, Does.Contain("OnStartEditMode -="));
	}

	[Test]
	public void NavHotPaths_NullGuardUserCamerasAndDims() {
		string orbit = Read("Assets", "_gm", "Features", "Camera", "Navigation", "CameraOrbit.cs");
		Assert.That(orbit, Does.Contain("if (UserCameras_MGR.instance == null) return"));
		Assert.That(orbit, Does.Contain("if (DimensionMode_MGR.instance == null) return"));
		string pan = Read("Assets", "_gm", "Features", "Camera", "Navigation", "CameraPanning.cs");
		Assert.That(pan, Does.Contain("DimensionMode_MGR.instance == null"));
		string focus = Read("Assets", "_gm", "Features", "Camera", "Navigation", "CameraFocus.cs");
		Assert.That(focus, Does.Contain("if (UserCameras_MGR.instance == null) return"));
	}

	[Test]
	public void VaeAndModels_FetchBusyClearedInFinally() {
		string vae = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_VAE.cs");
		Assert.That(vae, Does.Contain("finally"));
		Assert.That(vae, Does.Contain("_isFetchingVAEs = false"));
		Assert.That(vae, Does.Contain("hub == null || hub._generating"));
		string models = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel",
			"SD_Neural_Models.cs");
		Assert.That(models, Does.Contain("finally"));
		Assert.That(models, Does.Contain("_isFetchingModels = false"));
	}

	[Test]
	public void LayerFromTextures_RejectsNullFirstSlice() {
		string src = Read("Assets", "_gm", "Features", "Paint", "Layers", "PaintLayerStack_MGR.cs");
		Assert.That(src, Does.Contain("if (orderedTexList[0] == null)"));
	}

	[Test]
	public void ProjMaskPainter_NullGuardsMultiView() {
		string src = Read("Assets", "_gm", "Features", "Paint", "ProjectionsMasking",
			"Projections_MaskPainter.cs");
		Assert.That(src, Does.Contain("var mv = MultiView_Ribbon_UI.instance"));
		Assert.That(src, Does.Contain("if (mv == null) return"));
	}

	[Test]
	public void Gen3d_ClearsRetexturePrepOnDestroy() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		Assert.That(src, Does.Contain("_retexturePrepInFlight = false"));
	}
}
