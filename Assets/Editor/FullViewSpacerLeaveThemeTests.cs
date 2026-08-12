using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FullViewSpacerLeaveThemeTests {
	[Test]
	public void FullView_LeaveRestoresSpacerRow() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "RibbonViewportFullViewOnScreen_Toggle_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_spacerRowRt)"));
	}

	[Test]
	public void FullViewDriver_ResetsStaticsOnSubsystemRegistration() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "ViewportFullViewOnScreen_Driver.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ResetFullViewDriverStatics"));
		Assert.That(src, Does.Contain("RuntimeInitializeLoadType.SubsystemRegistration"));
		Assert.That(src, Does.Contain("ActiveChanged = null"));
	}

	[Test]
	public void FullViewDriver_ResolveBestScreenUsesPairedDisplaySize_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "ViewportFullViewOnScreen_Driver.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static Vector2Int ResolveBestScreenPixelSize", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(1800, src.Length - idx));
		Assert.That(body, Does.Contain("mainWindowDisplayInfo"));
		Assert.That(body, Does.Not.Contain("GetDisplayLayout"),
			"Must not Max W/H across all monitors (frankenstein multi-monitor size)");
		Assert.That(body, Does.Not.Contain("Display.displays"),
			"Must not Max across Display.displays independently");
		Assert.That(body, Does.Contain("di.width > 0 && di.height > 0"),
			"Accept mainWindowDisplayInfo only as a paired W×H");
	}
}
