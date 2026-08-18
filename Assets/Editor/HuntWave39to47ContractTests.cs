using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntWave39to47ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void ApplyUvTexture_UsesFirstEnabledPovMasks() {
		string src = Read("Assets", "_gm", "Features", "Render", "Objects_Renderer_MGR.cs");
		int i = src.IndexOf("void ApplyUvTexture(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("TryGetFirstEnabledPovMasks"));
		Assert.That(body, Does.Not.Contain("_ObjectUV_brushedMaskR8[0]"));
	}

	[Test]
	public void HdrPanoramic_AbortsWrongKindAndEmptyDepth() {
		string src = Read("Assets", "_gm", "Features", "Skybox + Background", "HDR_PanoSkybox_MGR.cs");
		int gen = src.IndexOf("public void Generate_PanoramicHDR(", StringComparison.Ordinal);
		string genBody = src.Substring(gen, Math.Min(500, src.Length - gen));
		Assert.That(genBody, Does.Contain("return;"));
		Assert.That(src, Does.Contain("response?.images == null || response.images.Length == 0"));
	}

	[Test]
	public void ModelsHandlerUi_UnsubscribesWillDestroyMesh() {
		string src = Read("Assets", "_gm", "Features", "3D Models", "ModelsHandler_3D_UI.cs");
		int d = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		string body = src.Substring(d, Math.Min(350, src.Length - d));
		Assert.That(body, Does.Contain("SD_3D_Mesh.Act_OnWillDestroyMesh -= OnWillDestroyMesh"));
	}

	[Test]
	public void Gen3dSupportedOps_NullGuardsSettingsMgr() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		int i = src.IndexOf("IEnumerator GetSupportedOperations_Looped_crtn()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(450, src.Length - i));
		Assert.That(body, Does.Contain("Settings_MGR.instance != null"));
	}

	[Test]
	public void WeightImport_RefusesOverwriteWithoutConfirmUi() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Input Panel",
			"SD_WeightFileImport.cs");
		Assert.That(src, Does.Contain("confirm UI unavailable"));
		Assert.That(src, Does.Contain("return;"));
	}

	[Test]
	public void SetMeshVisibility_RequiresVisibleBool() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		int i = src.IndexOf("case \"spz.cmd.set_mesh_visibility\":", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(550, src.Length - i));
		Assert.That(body, Does.Contain("visible bool required"));
		Assert.That(body, Does.Not.Contain("?? true"));
	}

	[Test]
	public void AgentDispatch_FailsOnNestedSuccessFalse() {
		string src = Read("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs");
		int i = src.IndexOf("static void DispatchSpzCmd(", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(1800, src.Length - i));
		Assert.That(body, Does.Contain("success:false"));
		Assert.That(body, Does.Contain("fail(jo[\"error\"]"));
	}

	[Test]
	public void Rembg_FinishUiIsIdempotentInFinally() {
		string src = Read("Assets", "_gm", "Features", "Paint", "BackgroundRemoval",
			"Rembg_PythonRunner.cs");
		Assert.That(src, Does.Contain("void FinishRembgUi(bool canceled)"));
		Assert.That(src, Does.Contain("if (finishedUi) return"));
		Assert.That(src, Does.Contain("finally"));
		Assert.That(src, Does.Contain("FinishRembgUi(canceled: true)"));
	}

	[Test]
	public void TextureExportDeferral_UsesLastSucceededFlag() {
		string save = Read("Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(save, Does.Contain("LastTextureDialogExportSucceeded"));
		string sock = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		Assert.That(sock, Does.Contain("LastTextureDialogExportSucceeded"));
		Assert.That(sock, Does.Contain("texture export cancelled or failed"));
	}
}
