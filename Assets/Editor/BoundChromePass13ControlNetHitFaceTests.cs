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
		// Pass36: keep header hit face transparent (SolidSquare Selectable covered "ControlNet N").
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_headerRibbon_button)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_headerRibbon_button)"));
		Assert.That(src, Does.Not.Contain(
			"_headerRibbon_button != null && _headerRibbon_button.targetGraphic != null"));
		int btnLoop = src.IndexOf("foreach (var btn in GetComponentsInChildren<Button>", System.StringComparison.Ordinal);
		Assert.That(btnLoop, Is.GreaterThan(0));
		string loopBody = src.Substring(btnLoop, System.Math.Min(500, src.Length - btnLoop));
		Assert.That(loopBody, Does.Not.Contain("btn.targetGraphic == null) continue"));
		Assert.That(loopBody, Does.Contain("ApplyBoundChromeSelectable(btn"));
	}

	[Test]
	public void SettingsAndInputPanel_SourcesDropNullTargetGraphicGates() {
		string settings = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Settings/Settings_UI.cs")));
		Assert.That(settings, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(settings, Does.Contain("ClearNonFaceRaycastsForTheme(btn)"));

		string input = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/StableDiffusion/Input Panel/SD_InputPanel_UI.cs")));
		int apply = input.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		string body = input.Substring(apply, System.Math.Min(2800, input.Length - apply));
		Assert.That(body, Does.Not.Contain("btn.targetGraphic == null) continue"));
		Assert.That(body, Does.Not.Contain("toggle.targetGraphic == null) continue"));
		Assert.That(body, Does.Not.Contain("dd.targetGraphic == null) continue"));
	}
}
