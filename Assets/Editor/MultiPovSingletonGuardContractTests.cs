using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Multi-POV projection helpers must not dereference ribbon/painter/save/camera singletons
/// without null checks — headless, reload, and early frames otherwise NRE mid-render.
/// </summary>
public sealed class MultiPovSingletonGuardContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void CursorMask_GuardsMultiViewPainterSaveAndWorkflow() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections",
			"ProjectorCameras_RenderHelper.cs");
		int i = src.IndexOf("void MultiPOV_Set_CursorMask(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("bool Set_ScreenArt_and_Mask(", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("MultiView_Ribbon_UI.instance"));
		Assert.That(body, Does.Contain("Projections_MaskPainter.instance"));
		Assert.That(body, Does.Contain("Save_MGR.instance"));
		Assert.That(body, Does.Contain("WorkflowRibbon_UI.instance"));
		Assert.That(body, Does.Contain("== null"),
			"must early-out when any required singleton is missing");
		Assert.That(body, Does.Not.Contain("Save_MGR.instance._isSaving"),
			"Save_MGR must be null-checked before _isSaving");
		Assert.That(body, Does.Not.Contain("MultiView_Ribbon_UI.instance.hoveredPovIx"));
	}

	[Test]
	public void HoverHighlight_GuardsPainterAndMultiView() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections",
			"ProjectorCameras_RenderHelper.cs");
		int i = src.IndexOf("void ShowSpecificPov_if_multipov_maybe(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("painter == null || mvRib == null"));
	}

	[Test]
	public void DummyTextMaker_GuardsRibbonsAndViewport() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections",
			"MultiProj_DummyTextMaker.cs");
		int i = src.IndexOf("void Update()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("oRib == null || mvRib == null || vp == null"));
		Assert.That(body, Does.Not.Contain("MultiView_Ribbon_UI.instance.currentPovIx"));
	}

	[Test]
	public void HsvcSetup_GuardsArt2DIconsList() {
		string src = Read("Assets", "_gm", "Features", "Camera", "Projections",
			"ProjectorCameras_RenderHelper.cs");
		Assert.That(src, Does.Not.Contain("Art2D_IconsUI_List.instance._mainSelectedIcon"),
			"Art2D list must be null-checked before _mainSelectedIcon");
		Assert.That(src, Does.Contain("artList != null"));
	}
}
