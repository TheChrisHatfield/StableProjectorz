using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// SD workflow options leave path must restore CircleSlider chrome outside the panel root
/// (esp. rethink mini next to GenArt).
/// </summary>
public sealed class SdWorkflowOptionsLeaveDialRestoreTests {

	[Test]
	public void LeavePath_SourceRestoresAllThemedCircleSliders() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/SD_WorkflowOptionsRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		Assert.That(apply, Is.GreaterThan(0));
		int leave = src.IndexOf("if (!SpzUiThemeOps.ShouldRecolorBoundChrome)", apply, System.StringComparison.Ordinal);
		Assert.That(leave, Is.GreaterThan(0));
		int themed = src.IndexOf("var t = SpzUiThemeOps.Active;", leave, System.StringComparison.Ordinal);
		Assert.That(themed, Is.GreaterThan(leave));
		string body = src.Substring(leave, themed - leave);
		Assert.That(body, Does.Contain("RestoreCircle(_reThink_slider_mini)"));
		Assert.That(body, Does.Contain("RestoreCircle(_reThink_slider)"));
		Assert.That(body, Does.Contain("RestoreCircle(_blur_slider)"));
		Assert.That(body, Does.Contain("RestoreCircle(_edgeThresh_slider)"));
		Assert.That(body, Does.Contain("RestoreCircle(_edgeThick_slider)"));
		Assert.That(src, Does.Contain("static void RestoreCircle(CircleSlider_Snapping_UI slider)"));
	}
}
