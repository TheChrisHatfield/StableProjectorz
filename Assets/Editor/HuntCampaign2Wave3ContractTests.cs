using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntCampaign2Wave3ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void CursorUpdate_NullGuardsDimensionMode() {
		string src = Read("Assets", "_gm", "Features", "Viewport", "Main Viewport", "Cursor_UI.cs");
		Assert.That(src, Does.Contain("DimensionMode_MGR.instance"));
		Assert.That(src, Does.Contain("if (dims == null) return"));
		Assert.That(src, Does.Contain("Gen3D_WorkflowOptionsRibbon_UI.instance != null"));
	}

	[Test]
	public void BrushOpacity_UnsubscribesAndGuardsViewport() {
		string src = Read("Assets", "_gm", "Features", "Paint", "BrushRibbon_UI",
			"BrushRibbon_UI_Opacity.cs");
		Assert.That(src, Does.Contain("MainViewport_UI.instance?.isCursorHoveringMe()"));
		Assert.That(src, Does.Contain("void OnDestroy()"));
		Assert.That(src, Does.Contain("_Act_OnModeChanged -= OnWorkflowModeChanged"));
	}

	[Test]
	public void ScreenMasker_NullGuardsWorkflowAndViewport() {
		string src = Read("Assets", "_gm", "Features", "Paint", "Inpaint", "Inpaint_ScreenMasker.cs");
		Assert.That(src, Does.Contain("WorkflowRibbon_UI.instance == null"));
		Assert.That(src, Does.Contain("MainViewport_UI.instance == null"));
	}

	[Test]
	public void PerformanceOptimize_NullGuardsHub() {
		string src = Read("Assets", "_gm", "Features", "Settings", "Performance_MGR.cs");
		Assert.That(src, Does.Contain("hub == null || hub._finalPreparations_beforeGen"));
	}

	[Test]
	public void Gen3dScreenshotToggle_NullGuards() {
		string src = Read("Assets", "_gm", "Features", "3D Generate",
			"Gen3D_WorkflowOptionsRibbon_UI.cs");
		Assert.That(src, Does.Contain("DimensionMode_MGR.instance"));
		Assert.That(src, Does.Contain("Screenshot_MGR.instance"));
		Assert.That(src, Does.Contain("Viewport_StatusText.instance?.ShowStatusText"));
	}

	[Test]
	public void ObjectsRenderer_NullGuardsProjectorAndViewCam() {
		string src = Read("Assets", "_gm", "Features", "Render", "Objects_Renderer_MGR.cs");
		Assert.That(src, Does.Contain("ProjectorCameras_MGR.instance?.HighlightProjCamera"));
		Assert.That(src, Does.Contain("if (view == null) return"));
	}

	[Test]
	public void UvNavigate_NullGuardsDimsAndPanCam() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Navigation",
			"Camera_UV_NavigateHelper.cs");
		Assert.That(src, Does.Contain("dims != null && dims._dimensionMode"));
		Assert.That(src, Does.Contain("if (cam == null) return"));
	}

	[Test]
	public void ExportProjection_RequiresIsDilate() {
		string sock = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		Assert.That(sock, Does.Contain("is_dilate bool required"));
		string http = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		Assert.That(http, Does.Contain("is_dilate bool required"));
	}

	[Test]
	public void Art2d_UnsubscribesBakeAndExport() {
		string src = Read("Assets", "_gm", "Features", "Icons", "IconUI_List_Art",
			"Art2D_IconsUI_List.cs");
		Assert.That(src, Does.Contain("Act_onBakeColors_button -= OnBakeColorsButton"));
		Assert.That(src, Does.Contain("OnExportAllArt_Icons_Button -= OnExportAllIcons_Button"));
		Assert.That(src, Does.Contain("painter == null"));
	}

	[Test]
	public void ShadowR_FinishUiFinally() {
		string src = Read("Assets", "_gm", "Features", "TextureTools", "Delight",
			"ShadowR_PythonRunner.cs");
		Assert.That(src, Does.Contain("FinishShadowRUi"));
		Assert.That(src, Does.Contain("finally"));
		Assert.That(src, Does.Contain("Settings_MGR.instance"));
	}

	[Test]
	public void Icon3d_AfterInstantiateGuardsNullGenData() {
		string src = Read("Assets", "_gm", "Features", "3D Generate", "Icon3D_UI.cs");
		Assert.That(src, Does.Contain("if (_genData == null)"));
	}
}
