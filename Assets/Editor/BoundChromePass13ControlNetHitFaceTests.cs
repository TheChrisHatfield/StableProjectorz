using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 13: ControlNet unit header/body must theme without null-targetGraphic early-outs (gen path).
/// </summary>
public sealed class BoundChromePass13ControlNetHitFaceTests {

	[Test]
	public void ControlNetUnit_SourceThemesHeaderWithoutTargetGraphicGate() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/Controlnet/ControlNetUnit_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(_headerRibbon_button"));
		Assert.That(src, Does.Not.Contain(
			"_headerRibbon_button != null && _headerRibbon_button.targetGraphic != null"));
		int btnLoop = src.IndexOf("foreach (var btn in GetComponentsInChildren<Button>", System.StringComparison.Ordinal);
		Assert.That(btnLoop, Is.GreaterThan(0));
		string loopBody = src.Substring(btnLoop, System.Math.Min(500, src.Length - btnLoop));
		Assert.That(loopBody, Does.Not.Contain("btn.targetGraphic == null) continue"));
		Assert.That(loopBody, Does.Contain("ApplyBoundChromeSelectable(btn"));
	}
}
