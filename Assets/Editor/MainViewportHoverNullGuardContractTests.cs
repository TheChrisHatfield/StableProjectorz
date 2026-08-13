using System.IO;
using NUnit.Framework;

/// <summary>
/// Viewport hover gates must tolerate missing intro/update/context singletons (boot / unload).
/// </summary>
public sealed class MainViewportHoverNullGuardContractTests {

	[Test]
	public void IsCursorHoveringMe_Source_NullGuardsIntroSingletons() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Viewport", "Main Viewport", "MainViewport_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool isCursorHoveringMe()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("void DiagnoseAddonModalViewportRaycast", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("CheckForUpdates_MGR.instance != null"));
		Assert.That(body, Does.Contain("WelcomeScreenNovices_MGR.instance != null"));
		Assert.That(body, Does.Contain("_viewportContextMenu_mgr != null"));
		Assert.That(body, Does.Contain("MainViewport_UI_EventListener.instance == null"));
	}

	[Test]
	public void IsCursorInsideMyWidth_Source_NullGuardsIntroSingletons() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Viewport", "Main Viewport", "MainViewport_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public bool IsCursorInside_my_width()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("CheckForUpdates_MGR.instance != null"));
		Assert.That(body, Does.Contain("WelcomeScreenNovices_MGR.instance != null"));
		Assert.That(body, Does.Contain("MainViewport_UI_EventListener.instance == null"));
	}
}
