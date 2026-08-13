using System.IO;
using NUnit.Framework;

public sealed class AddonManagerListViewportAndInstallWiringTests {

	[Test]
	public void ListViewport_UsesKnownGoodMaskShell_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		// Last-known-good list (898dd6a UX + Install). Do not require RectMask2D — that path
		// emptied the list and left a white square after the white-bar "fixes".
		Assert.That(src, Does.Contain("StichAddonManager_v12"),
			"Shell version must bump so broken v10/v11 panels rebuild to the known-good list.");
		Assert.That(src, Does.Contain("AddComponent<UnityEngine.UI.Mask>()"),
			"List Viewport clips with Mask (known-good create path).");
		Assert.That(src, Does.Contain("showMaskGraphic = false"),
			"Mask graphic must stay hidden so BoundChrome cannot paint a white bar.");
		Assert.That(src, Does.Contain("ProtectListViewportMaskGraphic"),
			"Theme must keep Mask.showMaskGraphic false after BoundChrome (white bar without killing the list).");
		Assert.That(src, Does.Not.Contain("EnsureListScrollViewportHealthy"),
			"Do not migrate Mask→RectMask2D at runtime (that broke list population).");
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

	[Test]
	public void ExpandChevronArrow_CreatesRectTransformUpFront_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("static void ApplyExpandChevronVisual(", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int next = src.IndexOf("static void ThemeShowInRibbonDial(", apply, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(apply));
		string body = src.Substring(apply, next - apply);
		Assert.That(body, Does.Contain("new GameObject(\"Arrow\", typeof(RectTransform), typeof(Image))"),
			"Arrow must be created with RectTransform up front — AddComponent<RectTransform> after caching Transform NREs under IL2CPP.");
		Assert.That(body, Does.Not.Contain("go.AddComponent<RectTransform>()"),
			"Do not AddComponent<RectTransform> after assigning arrowT = go.transform.");
		Assert.That(src, Does.Contain("CreateAddonListItem failed for"),
			"List refresh must catch per-row failures so one NRE cannot empty the manager.");
	}
}
