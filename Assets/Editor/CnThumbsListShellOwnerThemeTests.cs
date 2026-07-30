using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class CnThumbsListShellOwnerThemeTests {
	[Test]
	public void CnThumbsList_DoesNotDualThemeRootPanelShell() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"Controlnet", "ControlNetUnits_ThumbsList_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CommandRibbon_UI — skip dual root tint"));
		Assert.That(src, Does.Not.Contain("ApplyBoundChromeGraphic(rootImg, t.panelBg)"));
	}
}
