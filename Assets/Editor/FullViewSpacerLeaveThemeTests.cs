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
}
