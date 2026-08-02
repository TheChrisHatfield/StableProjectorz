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
		Assert.That(ch, Does.Not.Contain("init_images"));
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
		// Deny/payload still force-capture; interactable poll must not call CanCaptureMeshDepth.
		Assert.That(hub, Does.Contain("CanCaptureMeshDepth()"));
		Assert.That(hub, Does.Not.Contain("HasMeshDepthRt()"));
		string ch = Read("Assets", "_gm", "Features", "StableDiffusion", "Klein", "SD_KleinStructureChannel.cs");
		Assert.That(ch, Does.Contain("HasMeshDepthRt"));
		Assert.That(ch, Does.Contain("Do not call from per-frame"));
		Assert.That(ch, Does.Contain("TryCaptureMeshDepthDisposable"));
		Assert.That(ch, Does.Contain("Checks RT while the depth lock is still held"));
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
}
