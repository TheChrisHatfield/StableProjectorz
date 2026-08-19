using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntCampaign2Wave2ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void HttpSkyboxColor_RequiresIsTop() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		int i = src.IndexOf("Handles background/skybox requests", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("is_top bool required"));
		Assert.That(body, Does.Not.Contain("?? true"));
	}

	[Test]
	public void HttpSaveLoad_WaitsAndChecksLastSucceeded() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		Assert.That(src, Does.Contain("LastProjectSaveSucceeded"));
		Assert.That(src, Does.Contain("LastProjectLoadSucceeded"));
		Assert.That(src, Does.Contain("WaitForProjectLoadIdle_offMainThread"));
		Assert.That(src, Does.Contain("IsProjectSaveInFlight"));
	}

	[Test]
	public void HttpTextureExport_UsesLastTextureDialogExportSucceeded() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		Assert.That(src, Does.Contain("LastTextureDialogExportSucceeded"));
		Assert.That(src, Does.Contain("projection texture export timed out"));
	}

	[Test]
	public void FastPathSaveLoad_ReturnArmedNotUnconditionalTrue() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		int save = src.IndexOf("public bool SaveProject(", StringComparison.Ordinal);
		string sbody = src.Substring(save, Math.Min(1400, src.Length - save));
		Assert.That(sbody, Does.Contain("IsProjectSaveInFlight"));
		Assert.That(sbody, Does.Contain("return saveMGR.SaveLoadHelper != null && saveMGR.SaveLoadHelper.IsProjectSaveInFlight"));
		int load = src.IndexOf("public bool LoadProject(", StringComparison.Ordinal);
		string lbody = src.Substring(load, Math.Min(1400, src.Length - load));
		Assert.That(lbody, Does.Contain("return saveMGR._isLoading"));
	}

	[Test]
	public void Icon3dGenerate_WiresToGen3dTrigger() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Icon3D_UI.cs");
		Assert.That(src, Does.Contain("TryAssignImageForGenerate"));
		Assert.That(src, Does.Contain("Trigger3DGeneration"));
		Assert.That(src, Does.Not.Contain("/*perform actual generation here*/"));
	}

	[Test]
	public void Art2dGetTextures_RefusesWithNullNotEmptyList() {
		string src = Read("Assets", "_gm", "Features", "Icons", "IconUI_List_Art",
			"Art2D_IconsUI_List.cs");
		int i = src.IndexOf("GetTextures_FromAllIcons", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(1100, src.Length - i));
		Assert.That(body, Does.Contain("onReady_TexturesWithoutOwner?.Invoke(null)"));
		Assert.That(body, Does.Contain("Empty list was treated as merge OK"));
	}
}
