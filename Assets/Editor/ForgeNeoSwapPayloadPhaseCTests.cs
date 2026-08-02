using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;

/// <summary>
/// forge-neo-swap Phase C: Soft Inpaint positional scalars, no $type under alwayson, DataPath download gate.
/// </summary>
public sealed class ForgeNeoSwapPayloadPhaseCTests {

	[Test]
	public void SoftInpaint_ToPositionalArgs_IsSevenScalars() {
		object[] args = SoftInpaintingArgs.ToPositionalArgs(new SoftInpaintingArgsEntry());
		Assert.That(args.Length, Is.EqualTo(7));
		Assert.That(args[0], Is.TypeOf<bool>());
		Assert.That(args[1], Is.TypeOf<float>().Or.TypeOf<double>().Or.TypeOf<int>());
		string json = JsonConvert.SerializeObject(SoftInpaintingArgs.FromEntry(new SoftInpaintingArgsEntry()),
			new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
		JObject root = JObject.Parse(json);
		JArray arr = (JArray)root["args"];
		Assert.That(arr.Count, Is.EqualTo(7));
		Assert.That(arr[0].Type, Is.EqualTo(JTokenType.Boolean));
		for (int i = 1; i < 7; i++)
			Assert.That(arr[i].Type, Is.EqualTo(JTokenType.Float).Or.EqualTo(JTokenType.Integer),
				"args[" + i + "] must be a JSON number, not an object");
	}

	[Test]
	public void SoftInpaint_MustNotSerializeLabeledObjectAtArgs0() {
		string json = JsonConvert.SerializeObject(SoftInpaintingArgs.FromEntry(new SoftInpaintingArgsEntry()),
			new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
		Assert.That(json, Does.Not.Contain("Schedule bias"));
		Assert.That(json, Does.Not.Contain("Soft inpainting\":"));
	}

	[Test]
	public void GenerateSender_UsesTypeNameHandlingNone() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_NetworkSender.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TypeNameHandling.None"));
		Assert.That(src, Does.Not.Contain("TypeNameHandling.Auto //automatically resolve"));
	}

	[Test]
	public void AlwaysOn_ControlNet_SerializesWithoutTypeName() {
		var cn = new ControlNet_NetworkArgs {
			args = new[] {
				new ControlNetUnit_NetworkArgs {
					enabled = true,
					module = "depth",
					model = "control_v11f1p_sd15_depth",
					weight = 1f,
				}
			}
		};
		var payload = new SD_txt2img_payload {
			prompt = "t",
			alwayson_scripts = new System.Collections.Generic.Dictionary<string, AlwaysOn_Value> {
				{ "controlnet", cn }
			}
		};
		string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings {
			TypeNameHandling = TypeNameHandling.None,
			Formatting = Formatting.None
		});
		Assert.That(json, Does.Not.Contain("$type"));
		Assert.That(json, Does.Contain("\"enabled\":true"));
		Assert.That(json, Does.Contain("\"module\":\"depth\""));
	}

	[Test]
	public void Img2img_InitWithoutMask_SynthesizesFullWhiteMask() {
		string maker = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		string tools = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs"));
		Assert.That(tools, Does.Contain("CreateSolidColorRGBA32"));
		Assert.That(maker, Does.Contain("CreateSolidColorRGBA32("));
		Assert.That(maker, Does.Contain("viewTex.width, viewTex.height, Color.white"));
		Assert.That(maker, Does.Contain("viewTex != null && screenMask_skipAntiEdge == null"));
	}

	[Test]
	public void Img2img_KeepsNativeByproducts_EncodePairOnlyWhenMismatch() {
		string maker = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		string tools = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "TextureTools_SPZ.cs"));
		Assert.That(tools, Does.Contain("PrepareImg2ImgEncodePair"));
		Assert.That(tools, Does.Contain("FitTexture2D_CropAndResize_KeepSrc"));
		Assert.That(tools, Does.Contain("ResizeTexture2D_Exact_KeepSrc"));
		Assert.That(maker, Does.Contain("PrepareImg2ImgEncodePair"));
		Assert.That(maker, Does.Contain("Pre-Neo / SD1.5 path"));
		Assert.That(maker, Does.Contain("payloadW"));
		Assert.That(maker, Does.Contain("encodeInit.width"));
		// Must not stretch byproducts to panel WxH (broke projection after Neo workaround).
		Assert.That(maker, Does.Not.Contain("ResizeTexture2D_Exact_DestroySrc(viewTex, outW, outH)"));
	}

	[Test]
	public void Img2img_InvalidPanelSize_AbortsBeforePayloadEncode() {
		string src = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(src, Does.Contain("invalid width/height in SD input panel"));
		int badSize = src.IndexOf("outW <= 0 || outH <= 0");
		Assert.That(badSize, Is.GreaterThan(0));
		int earlyReturn = src.IndexOf("return;", badSize);
		int encodePair = src.IndexOf("PrepareImg2ImgEncodePair", badSize);
		Assert.That(earlyReturn, Is.GreaterThan(badSize));
		Assert.That(encodePair, Is.GreaterThan(earlyReturn),
			"Invalid WxH must return before encoding init+mask.");
	}

	[Test]
	public void Img2img_Klein_WhiteMaskOnlyOnProjectionsMasking() {
		string maker = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(maker, Does.Contain("Full-white only for ProjectionsMasking"));
		Assert.That(maker, Does.Not.Contain("|| !WorkflowRibbon_UI.instance.has_brushed_mask()"));
		Assert.That(maker, Does.Contain("currentMode() == WorkflowRibbon_CurrMode.ProjectionsMasking"));
	}

	[Test]
	public void Img2img_Klein_SkipsSoftInpaintScript() {
		string maker = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(maker, Does.Contain("!StableDiffusion_Hub.IsActiveCheckpointKlein()"));
		Assert.That(maker, Does.Contain("Soft Inpainting"));
		int kleinGate = maker.IndexOf("!StableDiffusion_Hub.IsActiveCheckpointKlein()");
		int softAdd = maker.IndexOf("alwayson_scripts.Add(\"Soft Inpainting\"");
		Assert.That(kleinGate, Is.GreaterThan(0));
		Assert.That(softAdd, Is.GreaterThan(kleinGate));
	}

	[Test]
	public void SoftInpaint_OnlyAddedOnImg2imgPath() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		string src = File.ReadAllText(path);
		int softIdx = src.IndexOf("Soft Inpainting");
		Assert.That(softIdx, Is.GreaterThan(0));
		// Soft Inpaint add must sit in img2img builder, not before first Create_txt2img / make_txt2img.
		int img2imgMarker = src.IndexOf("Create_img2img_payload");
		if (img2imgMarker < 0) img2imgMarker = src.IndexOf("make_img2img");
		Assert.That(img2imgMarker, Is.GreaterThan(0));
		Assert.That(softIdx, Is.GreaterThan(img2imgMarker),
			"Soft Inpainting alwayson must only be wired on img2img path (Neo img2img-only).");
	}

	[Test]
	public void SysInfo_TryResolveControlNetModelsDir_RequiresDataPath() {
		// Pure helper: with no instance / empty path, resolve fails (cannot call instance methods easily).
		// Contract: source contains gate + forge-neo-swap R2 helpers.
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "SD_SysInfo_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryGetSdDataPath"));
		Assert.That(src, Does.Contain("TryResolveControlNetModelsDir"));
		Assert.That(src, Does.Contain("isForgeFamilyWebui_detected"));
	}

	[Test]
	public void SysInfo_ForgeFamilyDetect_RecognizesBareNeoCheckoutPath() {
		// True Haoming02 DataPath often ends in \neo without substring "forge" (triangulation G1).
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "SD_SysInfo_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PathLooksForgeFamily"));
		Assert.That(src, Does.Contain("EndsWith(\"/neo\")"));
		Assert.That(src, Does.Contain("StartsWith(\"neo\""));
		Assert.That(src, Does.Contain("forge_neo_true").Or.Contain("/neo/"));
	}

	[Test]
	public void DownloadAndOpenUrl_GateOnEmptyDataPath() {
		string dl = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "IO", "Download", "DownloadFile_if_NotYetExist.cs");
		string open = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "IO", "Download", "OpenURL_and_Subdirectory.cs");
		Assert.That(File.ReadAllText(dl), Does.Contain("TryResolveControlNetModelsDir"));
		Assert.That(File.ReadAllText(open), Does.Contain("TryResolveControlNetModelsDir"));
	}

	[Test]
	public void MaskPainter_UsesFromEntryPositional() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Inpaint", "Inpaint_MaskPainter.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SoftInpaintingArgs.FromEntry"));
		Assert.That(src, Does.Not.Contain("new SoftInpaintingArgsEntry[1]"));
	}

	[Test]
	public void ControlNet_PreprocessorNone_NormalizedForNeoCaseSensitiveLookup() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_Dropdowns.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("currPreprocessorName"));
		Assert.That(src, Does.Contain("Equals(\"none\""));
		Assert.That(src, Does.Contain("return \"None\""));
		Assert.That(src, Does.Contain("new TMP_Dropdown.OptionData(\"None\")"));
	}

	[Test]
	public void ControlNet_DetectAndNetworkArgs_DefaultModuleIsCapitalNone() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Serialization", "SD_JSON_Payloads.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("controlnet_module = \"None\""));
		Assert.That(src, Does.Contain("module = \"None\""));
		Assert.That(src, Does.Not.Contain("controlnet_module = \"none\""));
	}

	[Test]
	public void ControlNetUnit_GenArgs_UsesCurrPreprocessorName() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("module = _preprocessor.currPreprocessorName()"));
	}

	[Test]
	public void OptionsPost_OmitsHiresFixRefinerPass_ForNeoCompat() {
		var opt = new SD_OptionsPacket {
			sd_model_checkpoint = "realisticvisionv51_v51vae.safetensors",
			sd_vae = "None",
			hires_fix_refiner_pass = "first pass",
			tiling = false,
		};
		string json = opt.ToOutboundJson();
		Assert.That(json, Does.Contain("sd_model_checkpoint"));
		Assert.That(json, Does.Contain("tiling"));
		Assert.That(json, Does.Not.Contain("hires_fix_refiner_pass"));
		Assert.That(File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Options_Fetcher.cs")),
			Does.Contain("ToOutboundJson()"));
	}

	[Test]
	public void OptionsPost_KleinModules_WhenCheckpointNeedsThem() {
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("flux-2-klein-4b"), Is.True);
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("FLUX.2-klein-4B.safetensors"), Is.True);
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("realisticvisionv51_v51vae"), Is.False);
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("flux-2-dev"), Is.False);
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("flux2"), Is.False);
		Assert.That(SD_OptionsPacket.CheckpointNeedsKleinModules("some-klein-only"), Is.False);
		var opt = new SD_OptionsPacket {
			sd_model_checkpoint = "flux-2-klein-4b.safetensors",
			tiling = false,
			forge_additional_modules = new[] {
				SD_OptionsPacket.KleinTextEncoderModule,
				SD_OptionsPacket.KleinVaeModule,
			},
		};
		string json = opt.ToOutboundJson();
		Assert.That(json, Does.Contain("forge_additional_modules"));
		Assert.That(json, Does.Contain(SD_OptionsPacket.KleinTextEncoderModule));
		Assert.That(json, Does.Contain(SD_OptionsPacket.KleinVaeModule));
		Assert.That(File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Neural_Models.cs")),
			Does.Contain("CheckpointNeedsKleinModules"));
	}

	[Test]
	public void ControlNet_FamilyMismatch_XlCnWithSd15Detected() {
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"diffusers_xl_depth_full", "realisticvisionv51_v51vae.safetensors"), Is.True);
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"control_v11f1p_sd15_depth", "realisticvisionv51_v51vae.safetensors"), Is.False);
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"diffusers_xl_depth_full", "juggernautXL_ragnarokBy.safetensors"), Is.False);
	}

	[Test]
	public void Hub_Klein_DeniesEmptyCustomFileCoOpt() {
		string hub = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs"));
		string list = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs"));
		Assert.That(list, Does.Contain("HasArmedEmptyKleinCustomFile"));
		Assert.That(hub, Does.Contain("HasArmedEmptyKleinCustomFile"));
		Assert.That(hub, Does.Contain("!SD_ControlNetsList_UI.instance.HasKleinImg2ImgInitSource()"));
		Assert.That(hub, Does.Contain("CustomFile but no image is loaded"));
		Assert.That(list, Does.Contain("Disarm leftover empty CustomFile slots"));
	}

	[Test]
	public void ControlNet_Klein_PrefersDepthThenCustomFileThenContentCam() {
		string src = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs"));
		Assert.That(src, Does.Contain("Prefers mesh Depth (structure) over CustomFile (style) over ContentCam"));
		int peekDepth = src.IndexOf("TryPeekKleinInit(WhatImageToSend_CTRLNET.Depth");
		int peekFile = src.IndexOf("TryPeekKleinInit(WhatImageToSend_CTRLNET.CustomFile");
		int peekCam = src.IndexOf("TryPeekKleinInit(WhatImageToSend_CTRLNET.ContentCam");
		int pickDepth = src.IndexOf("TryPickKleinInit(WhatImageToSend_CTRLNET.Depth");
		int pickFile = src.IndexOf("TryPickKleinInit(WhatImageToSend_CTRLNET.CustomFile");
		int pickCam = src.IndexOf("TryPickKleinInit(WhatImageToSend_CTRLNET.ContentCam");
		Assert.That(peekDepth, Is.GreaterThan(0));
		Assert.That(peekFile, Is.GreaterThan(peekDepth), "Peek must try Depth before CustomFile.");
		Assert.That(peekCam, Is.GreaterThan(peekFile), "Peek must try CustomFile before ContentCam.");
		Assert.That(pickDepth, Is.GreaterThan(0));
		Assert.That(pickFile, Is.GreaterThan(pickDepth), "Pick must try Depth before CustomFile.");
		Assert.That(pickCam, Is.GreaterThan(pickFile), "Pick must try CustomFile before ContentCam.");
		string imgs = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_ImagesDisplay.cs"));
		Assert.That(imgs, Does.Contain("WhatImageToSend_CTRLNET.Depth"));
		Assert.That(imgs, Does.Contain("GetDisposable_DepthTexture"));
		Assert.That(imgs, Does.Contain("_SD_depthCam_RT_R32_contrast != null"));
		Assert.That(imgs, Does.Contain("_contentCam_RT_ref != null"));
		Assert.That(imgs, Does.Contain("content_depthRender"));
		Assert.That(imgs, Does.Contain("LockOrUnlock_ByType(CameraTexType.DepthUserCamera"));
		string camTex = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "UserCameras_MGR_CamTextures.cs"));
		Assert.That(camTex, Does.Contain("_SD_depthCam_RT_R32_contrast == null"));
		string payload = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(payload, Does.Contain("kleinUsesDedicatedInit"));
		Assert.That(payload, Does.Contain("TryGetDisposableKleinImg2ImgInitForLabel"));
		Assert.That(payload, Does.Contain("do not substitute ContentCam"));
		Assert.That(payload, Does.Contain("Klein {kleinSrcLabel} init unavailable"));
		Assert.That(payload, Does.Contain("Dedicated Klein init abort leaves viewTex null"));
		Assert.That(payload, Does.Contain("\"Depth\""));
		Assert.That(src, Does.Contain("TryGetDisposableKleinImg2ImgInitForLabel"));
		Assert.That(src, Does.Contain("Avoids silently substituting CustomFile"));
	}

	[Test]
	public void ControlNet_Klein_RejectsAllCnWeights_IncludingFunUnion_AndBypassesDepthGate() {
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"diffusers_xl_depth_full", "flux-2-klein-4b"), Is.True);
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"control_v11f1p_sd15_depth", "flux-2-klein-4b.safetensors"), Is.True);
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"None", "flux-2-klein-4b"), Is.False);
		Assert.That(ControlNetUnit_Dropdowns.ControlNetModelLooksFlux2(
			"FLUX.2-dev-Fun-Controlnet-Union.safetensors"), Is.True);
		Assert.That(ControlNetUnit_Dropdowns.ControlNetModelLooksFlux2(
			"some-controlnet-union.safetensors"), Is.False,
			"Bare controlnet-union without flux/fun marker must not match.");
		Assert.That(ControlNetUnit_Dropdowns.FindPreferredFlux2ModelIndex(new[] {
			"other.safetensors",
			"flux2-alt.safetensors",
			"FLUX.2-dev-Fun-Controlnet-Union.safetensors",
		}), Is.EqualTo(2));
		// Fun-Union is for FLUX.2-dev, not Klein-4B — treat as mismatch so GetArgs never sends it.
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"FLUX.2-dev-Fun-Controlnet-Union.safetensors", "flux-2-klein-4b"), Is.True);
		Assert.That(ControlNetUnit_Dropdowns.IsControlNetCheckpointFamilyMismatch(
			"FLUX.2-dev-Fun-Controlnet-Union.safetensors", "realisticvisionv51_v51vae"), Is.True);
		string hub = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs"));
		Assert.That(hub, Does.Contain("IsActiveCheckpointKlein()"));
		Assert.That(hub, Does.Contain("has_Depth_or_Norm_or_RefOnly() || kleinReady"));
		Assert.That(hub, Does.Contain("HasKleinImg2ImgInitSource()"));
		Assert.That(hub, Does.Contain("Klein Gen Art needs an img2img init"));
		Assert.That(hub, Does.Contain("!klein && has_Depth_or_Norm_or_RefOnly()==false"));
		string list = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs"));
		Assert.That(list, Does.Contain("TryApplyKleinControlNetLayout"));
		Assert.That(list, Does.Contain("no Fun-Union ControlNet"));
		Assert.That(list, Does.Contain("ClearAllUnitModelsToNone"));
		Assert.That(list, Does.Contain("leftover Depth/ContentCam/CustomFile would still force Klein img2img"));
		Assert.That(list, Does.Contain("WhatImageToSend_CTRLNET.Depth"));
		Assert.That(list, Does.Contain("IsUnitModelValidForActiveCheckpoint"));
		Assert.That(list, Does.Contain("TryHealFamilyMismatchedModels"));
		Assert.That(list, Does.Contain("Klein-4B: no compatible CN — disarm models to None"));
		Assert.That(list, Does.Contain("must not leave Gen Art gated with no init"));
		Assert.That(list, Does.Contain("Capture role before model swap"));
		Assert.That(list, Does.Contain("Refuse a \"heal\" that would still mismatch"));
		string dropdowns = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_Dropdowns.cs"));
		Assert.That(dropdowns, Does.Contain("Do not auto-pick any CN weight"));
		Assert.That(dropdowns, Does.Contain("no alwayson ControlNet (Fun-Union ineffective)"));
		Assert.That(dropdowns, Does.Contain("drop legacy depth_* preprocessors"));
		string neural = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Neural_Models.cs"));
		Assert.That(neural, Does.Contain("TryHealFamilyMismatchedModels"));
		string unitUi = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs"));
		Assert.That(unitUi, Does.Contain("no control image available for this unit"));
		Assert.That(unitUi, Does.Contain("force None at payload time"));
		Assert.That(unitUi, Does.Contain("uses Depth/CustomFile img2img init"));
		string payload = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs"));
		Assert.That(payload, Does.Contain("SD_ControlNetsList_UI.instance != null"));
		Assert.That(payload, Does.Contain("ctrlNets_args != null && ctrlNets_args.args != null"));
		string hubTxt = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs"));
		Assert.That(hubTxt, Does.Contain("Fun-Union ControlNet is not used for Klein-4B"));
		string agent = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs"));
		Assert.That(agent, Does.Contain("klein_depth_img2img_armed"));
		Assert.That(agent, Does.Contain("depth-as-img2img layout"));
		Assert.That(agent, Does.Contain("Legacy key: true only when Klein Gen Art is actually armed"));
		Assert.That(agent, Does.Contain("HasKleinImg2ImgInitSource()"));
	}

	[Test]
	public void ControlNet_ModelNone_NotForcedBackToDepth_OnRefresh() {
		string src = File.ReadAllText(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_Dropdowns.cs"));
		Assert.That(src, Does.Not.Contain("prevChoice.ToLower()==\"none\")"));
		Assert.That(src, Does.Contain("pickDepth_ifWasNone &= string.IsNullOrEmpty(prevChoice)"));
		Assert.That(src, Does.Contain("CheckpointNeedsKleinModules(sd)"));
		Assert.That(src, Does.Contain("chosen.Equals(\"none\""));
		Assert.That(src, Does.Not.Contain("Contains(\"none\") ? \"None\""));
	}
}
