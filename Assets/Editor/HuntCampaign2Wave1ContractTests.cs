using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntCampaign2Wave1ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void UvWarpUpdate_NullGuardsCameraChain() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Navigation",
			"UserCameras_UV_warp_Helper.cs");
		int i = src.IndexOf("void Update()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("UserCameras_MGR.instance"));
		Assert.That(body, Does.Contain("Settings_MGR.instance"));
		Assert.That(body, Does.Contain("DimensionMode_MGR.instance"));
		Assert.That(body, Does.Contain("if (cam == null || settings == null || dims == null) return"));
	}

	[Test]
	public void CameraInfoHud_NullGuardsSettingsAndModels() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Info", "Camera_InfoText_UI.cs");
		Assert.That(src, Does.Contain("Settings_MGR.instance == null"));
		Assert.That(src, Does.Contain("ModelsHandler_3D.instance"));
		Assert.That(src, Does.Contain("if (mh == null) return"));
	}

	[Test]
	public void CameraDolly_NullGuardsNavManagers() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Navigation", "CameraDolly.cs");
		int i = src.IndexOf("void OnUpdate()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(550, src.Length - i));
		Assert.That(body, Does.Contain("cams == null || dims == null"));
	}

	[Test]
	public void SkyboxUpdate_NullGuardsSettingsMgr() {
		string src = Read("Assets", "_gm", "Features", "Icons", "SkyboxBackground_MGR.cs");
		Assert.That(src, Does.Contain("var settings = Settings_MGR.instance"));
		Assert.That(src, Does.Contain("if (settings != null)"));
	}

	[Test]
	public void AoBake_InterruptSkipsCommitAndSuccess() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "AO",
			"AmbientOcclusion_Baker.cs");
		Assert.That(src, Does.Contain("if (_interruptBake_asap)"));
		Assert.That(src, Does.Contain("Interrupt must not still blur"));
		int interrupt = src.IndexOf("if (_interruptBake_asap)", StringComparison.Ordinal);
		int okTrue = src.IndexOf("ok = true;", interrupt, StringComparison.Ordinal);
		Assert.That(okTrue, Is.GreaterThan(interrupt));
		string between = src.Substring(interrupt, okTrue - interrupt);
		Assert.That(between, Does.Contain("ok = false"));
	}
}
