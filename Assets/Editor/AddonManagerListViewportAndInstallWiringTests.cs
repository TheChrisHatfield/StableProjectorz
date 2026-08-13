using System.IO;
using NUnit.Framework;

public sealed class AddonManagerListViewportAndInstallWiringTests {

	[Test]
	public void ListViewport_UsesRectMask2DNotStencilMask_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureListScrollViewportHealthy"),
			"Open/theme must migrate legacy Mask viewports that paint a white bar.");
		Assert.That(src, Does.Contain("AddComponent<RectMask2D>()"),
			"List Viewport must clip with RectMask2D.");
		Assert.That(src, Does.Contain("SolidSquare on the mask graphic paints a white vertical bar"),
			"Create path must document why Mask+Image is forbidden.");
	}

	[Test]
	public void InstallFromFile_UsesDeferredHelperAndHeaderRebind_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureHeaderActionButtonsWired"),
			"Install must be re-bound on recovered shells.");
		Assert.That(src, Does.Contain("AddonInstallFromFile_Helper.CoDeferredThenPickZipOrInitPy"),
			"Install must open the file browser above the manager after pointer-up.");
		Assert.That(src, Does.Contain("Bind(\"InstallButton\", ref _installFromFile_button, OnInstallFromFile"));
	}
}
