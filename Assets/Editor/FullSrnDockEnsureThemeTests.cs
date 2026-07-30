using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FullSrnDockEnsureThemeTests {
	[Test]
	public void FullSrn_DockAndOpenRightEnsureHitFaceBeforeClear() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "RibbonViewportFullViewOnScreen_Toggle_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_dockButton)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(openBtn)"));
	}
}
