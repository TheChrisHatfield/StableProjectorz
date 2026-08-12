using System;
using System.IO;
using NUnit.Framework;
using spz;
using UnityEngine;

/// <summary>
/// Klein depth orchestration: structure channel (ImageStitch), never Depth-as-init;
/// similarity guard + dev traceback contracts.
/// Spec/micro: docs/delta/20_micro/klein-structure-channel.md
/// </summary>
public sealed class KleinStructureChannelContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void StructureChannel_UsesImageStitch_NotFunUnion() {
		string ch = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "SD_KleinStructureChannel.cs");
		Assert.That(ch, Does.Contain("imagestitch integrated"));
		Assert.That(ch, Does.Contain("mesh_depth_content_frustum"));
		Assert.That(ch, Does.Contain("GetDisposable_DepthTexture"));
		Assert.That(ch, Does.Contain("ImageStitch_AlwaysOnArgs"));
		Assert.That(ch, Does.Contain("LooksLikeDepthPlate"));
		Assert.That(ch, Does.Contain("data:image/png;base64,"));
		Assert.That(ch, Does.Contain("TryCaptureMeshDepthDisposable"));
		Assert.That(ch, Does.Contain("flux2_klein_4b_refcontrol_depth"));
		Assert.That(ch, Does.Contain("AppendRefControlToPrompt"));
		Assert.That(ch, Does.Contain("FromReferenceBase64List"));
		Assert.That(ch, Does.Contain("style_ref_missing"));
		Assert.That(ch, Does.Contain("TryGetDisposableLoadedCustomFileBitmap"));
		// Loaded CustomFile (even deactivated) before ContentCam for intentional style refs.
		int customFile = ch.IndexOf("kind = \"CustomFile\"", StringComparison.Ordinal);
		int contentCam = ch.IndexOf("kind = style != null ? \"ContentCam\"", StringComparison.Ordinal);
		Assert.That(customFile, Is.GreaterThanOrEqualTo(0));
		Assert.That(contentCam, Is.GreaterThan(customFile));
		Assert.That(ch, Does.Contain("(float)maxSide"));
		Assert.That(ch, Does.Not.Contain("init_images"));
		string payload = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		Assert.That(payload, Does.Contain("AppendRefControlToPrompt"));
		string listUi = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "SD_ControlNetsList_UI.cs");
		Assert.That(listUi, Does.Contain("TryGetDisposableLoadedCustomFileBitmap"));
	}

	[Test]
	public void Payload_AttachesStructure_SkipsControlNetOnKleinGenArt() {
		string payload = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		Assert.That(payload, Does.Contain("TryAttachMeshDepthStructure"));
		Assert.That(payload, Does.Contain("kleinGenArt"));
		Assert.That(payload, Does.Contain("TryHealFamilyMismatchedModels"));
		Assert.That(payload, Does.Contain("Depth is never pixel init"));
		Assert.That(payload, Does.Contain("\"CustomFile\""));
		Assert.That(payload.IndexOf("TryPeekKleinInit(WhatImageToSend_CTRLNET.Depth", StringComparison.Ordinal),
			Is.LessThan(0));
	}

	[Test]
	public void Hub_KleinGenArtEnable_IsCheckpoint_StructureOnDeny() {
		string hub = Read("Assets", "_gm", "Features", "StableDiffusion", "StableDiffusion_Hub.cs");
		Assert.That(hub, Does.Contain("bool kleinReady = IsActiveCheckpointKlein();"));
		Assert.That(hub, Does.Contain("ImageStitch"));
		Assert.That(hub, Does.Contain("never Depth"));
		Assert.That(hub, Does.Not.Contain("Klein Gen Art needs an img2img init"));
		// Neo prepends img2img ini_latent ahead of ImageStitch — Gen Art must stay txt2img.
		Assert.That(hub, Does.Contain("bool kleinGenArt = !isMakingBackgrounds && IsActiveCheckpointKlein();"));
		Assert.That(hub, Does.Contain("do_img2Img = false"));
		Assert.That(hub, Does.Contain("ini_latent"));
		Assert.That(hub, Does.Not.Contain("kleinPixelInit"));
		Assert.That(hub, Does.Contain("flux2_klein_4b_vae"));
		Assert.That(hub, Does.Contain("EnsureKleinSdVaeSelected"));
		string agent = Read("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs");
		Assert.That(agent, Does.Contain("[\"vae\"] = \"flux2_klein_4b_vae\""));
		// Deny/payload still force-capture; interactable poll must not call CanCaptureMeshDepth.
		Assert.That(hub, Does.Contain("CanCaptureMeshDepth()"));
		Assert.That(hub, Does.Not.Contain("HasMeshDepthRt()"));
		string ch = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "SD_KleinStructureChannel.cs");
		Assert.That(ch, Does.Contain("HasMeshDepthRt"));
		Assert.That(ch, Does.Contain("Do not call from per-frame"));
		Assert.That(ch, Does.Contain("TryCaptureMeshDepthDisposable"));
		Assert.That(ch, Does.Contain("Checks RT while the depth lock is still held"));
		Assert.That(ch, Does.Contain("skipUsualViewReuse"));
		Assert.That(ch, Does.Contain("synthetic_albedo_seed"));
		Assert.That(ch, Does.Contain("IsUsableStyleRef"));
		// RefControl HF: reference left, depth right → style then depth in ImageStitch list.
		Assert.That(ch, Does.Contain("styleB64, depthB64"));
	}

	[Test]
	public void InpaintBakeMask_CapturesBrushMaskOnKleinTxt2img() {
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.Inpaint_Color), Is.True);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.Inpaint_NoColor), Is.True);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.TotalObject), Is.True);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.WhereEmpty), Is.True);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: true,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.Inpaint_Color), Is.False);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: false,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.Inpaint_Color), Is.False);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: true,
			mode: WorkflowRibbon_CurrMode.ProjectionsMasking), Is.False);
		Assert.That(SD_KleinStructureChannel.ShouldCaptureInpaintBakeMaskForTxt2img(
			isMakingBackgrounds: false,
			isKleinCheckpoint: true,
			isImg2ImgWorkflowMode: false,
			mode: WorkflowRibbon_CurrMode.Inpaint_Color), Is.False);

		string payload = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		Assert.That(payload, Does.Contain("CaptureKleinTxt2imgInpaintBakeMask"));
		Assert.That(payload, Does.Contain("ShouldCaptureInpaintBakeMaskForTxt2img"));
		Assert.That(payload, Does.Contain("kleinTxt2imgInpaintBakeMask"));
		Assert.That(payload, Does.Contain("kleinSoftInpaintBlend"));
		Assert.That(payload, Does.Contain("kleinInpaintBlendStrength"));
		string results = Read("Assets", "_gm", "Features", "StableDiffusion", "GenData", "GenData_ResultTextures.cs");
		Assert.That(results, Does.Contain("TryApplyKleinTxt2imgInpaintComposite"));
		Assert.That(results, Does.Contain("kleinTxt2imgInpaintBakeMask"));
		string paintUi = Read("Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(paintUi, Does.Contain("WorkflowRibbon_CurrMode.WhereEmpty"));
	}

	[Test]
	public void StyleRef_GrayTexture_IsNotUsable() {
		var gray = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		var depth = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		try {
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++) {
					float g = (x + y) / 14f;
					gray.SetPixel(x, y, new Color(g, g, g, 1f));
					depth.SetPixel(x, y, new Color(g, g, g, 1f));
				}
			gray.Apply();
			depth.Apply();
			Assert.That(SD_KleinStructureChannel.IsUsableStyleRef(gray, depth), Is.False);
			var synth = SD_KleinStructureChannel.MakeSyntheticAlbedoStyle(64, 64);
			try {
				Assert.That(synth, Is.Not.Null);
				Assert.That(SD_KleinStructureChannel.IsUsableStyleRef(synth, depth), Is.True);
				Assert.That(SD_KleinStructureChannel.MeanChroma01(synth), Is.GreaterThan(0.04f));
			} finally {
				if (synth != null) UnityEngine.Object.DestroyImmediate(synth);
			}
		} finally {
			UnityEngine.Object.DestroyImmediate(gray);
			UnityEngine.Object.DestroyImmediate(depth);
		}
	}

	[Test]
	public void Guard_RejectsDepthLikeResultBeforeBake() {
		string helper = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		Assert.That(helper, Does.Contain("RejectKleinDepthLikeResult"));
		Assert.That(helper, Does.Contain("LooksLikeDepthPlate"));
		Assert.That(helper, Does.Contain("result looks like depth plate"));
		Assert.That(helper, Does.Contain("OnTerminatedGeneration(_latestGenData)"));
		Assert.That(helper, Does.Contain("Complete_PendingImages(null) is a no-op"));
	}

	[Test]
	public void Trace_DefaultOff_PrefsGated() {
		string trace = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "KleinStructureTrace.cs");
		Assert.That(trace, Does.Contain("spz.klein.structure_trace.v1"));
		Assert.That(trace, Does.Contain("ForceEnableForProbe"));
		Assert.That(trace, Does.Contain("PlayerPrefs.GetInt(PrefsKey, 0)"));
		Assert.That(trace, Does.Contain("return false"));
	}

	[Test]
	public void Similarity_IdenticalTextures_LookLikeDepthPlate() {
		var a = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		var b = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		try {
			var gray = new Color(0.4f, 0.4f, 0.4f, 1f);
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++) {
					a.SetPixel(x, y, gray);
					b.SetPixel(x, y, gray);
				}
			a.Apply();
			b.Apply();
			Assert.That(SD_KleinStructureChannel.LooksLikeDepthPlate(a, b, out float diff), Is.True);
			Assert.That(diff, Is.LessThan(0.08f));
		} finally {
			UnityEngine.Object.DestroyImmediate(a);
			UnityEngine.Object.DestroyImmediate(b);
		}
	}

	[Test]
	public void Similarity_DifferentTextures_DoNotLookLikeDepthPlate() {
		var a = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		var b = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		try {
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++) {
					a.SetPixel(x, y, Color.black);
					b.SetPixel(x, y, Color.white);
				}
			a.Apply();
			b.Apply();
			Assert.That(SD_KleinStructureChannel.LooksLikeDepthPlate(a, b, out float diff), Is.False);
			Assert.That(diff, Is.GreaterThan(0.08f));
		} finally {
			UnityEngine.Object.DestroyImmediate(a);
			UnityEngine.Object.DestroyImmediate(b);
		}
	}

	[Test]
	public void Similarity_NearGrayRemap_StillLooksLikeDepthPlate() {
		var depth = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		var remap = new Texture2D(8, 8, TextureFormat.RGBA32, false);
		try {
			for (int y = 0; y < 8; y++)
				for (int x = 0; x < 8; x++) {
					float g = (x + y) / 14f;
					depth.SetPixel(x, y, new Color(g, g, g, 1f));
					float g2 = Mathf.Clamp01(g * 0.5f + 0.25f); // level remap past 0.08 luma MAD, still gray
					remap.SetPixel(x, y, new Color(g2, g2, g2, 1f));
				}
			depth.Apply();
			remap.Apply();
			Assert.That(SD_KleinStructureChannel.LooksLikeDepthPlate(remap, depth, out float diff), Is.True);
			Assert.That(diff, Is.GreaterThanOrEqualTo(0.08f)); // would miss tight luma-only gate
			Assert.That(SD_KleinStructureChannel.MeanChroma01(remap), Is.LessThan(0.035f));
		} finally {
			UnityEngine.Object.DestroyImmediate(depth);
			UnityEngine.Object.DestroyImmediate(remap);
		}
	}

	[Test]
	public void Trace_LastRejectReason_UpdatesWhenTraceDisabled() {
		string trace = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "KleinStructureTrace.cs");
		Assert.That(trace, Does.Contain("LastRejectReason"));
		Assert.That(trace, Does.Contain("key == \"reject_reason\""));
		Assert.That(trace, Does.Contain("EnsureRequestStarted"));
		string payload = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		Assert.That(payload, Does.Contain("LastRejectReason"));
		Assert.That(payload, Does.Contain("style_ref_missing"));
		// Begin before Append so attach cannot wipe RefControl keys.
		Assert.That(payload, Does.Contain("KleinStructureTrace.BeginRequest()"));
		int begin = payload.IndexOf("KleinStructureTrace.BeginRequest()", StringComparison.Ordinal);
		int append = payload.IndexOf("AppendRefControlToPrompt", StringComparison.Ordinal);
		Assert.That(begin, Is.GreaterThanOrEqualTo(0));
		Assert.That(append, Is.GreaterThan(begin));
		string ch = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "SD_KleinStructureChannel.cs");
		Assert.That(ch, Does.Contain("EnsureRequestStarted"));
		Assert.That(ch, Does.Not.Contain("KleinStructureTrace.BeginRequest();"));
	}

	[Test]
	public void OnGeneratedResult_RejectsKleinBeforeSuccessFinish() {
		string helper = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		int onGen = helper.IndexOf("void OnGeneratedResult(", StringComparison.Ordinal);
		Assert.That(onGen, Is.GreaterThan(0));
		string body = helper.Substring(onGen, Math.Min(1800, helper.Length - onGen));
		int reject = body.IndexOf("RejectKleinDepthLikeResult", StringComparison.Ordinal);
		int successFinish = body.IndexOf("OnConfirmed_FinishedGenerate(canceled:false)", StringComparison.Ordinal);
		Assert.That(reject, Is.GreaterThan(0));
		Assert.That(successFinish, Is.GreaterThan(reject),
			"Success finish must run only after Klein reject check passes.");
		Assert.That(body, Does.Contain("OnConfirmed_FinishedGenerate(canceled:true)"),
			"Klein reject must finish UI as canceled, not success.");
	}
}
