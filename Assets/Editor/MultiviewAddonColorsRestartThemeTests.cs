using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class MultiviewNumCamsSliderOwnerThemeTests {
	[Test]
	public void MultiviewRibbon_DoesNotDualApplyNomadSliderChrome() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SliderUI_Snapping owns ApplyNomadSliderChrome"));
		Assert.That(src, Does.Not.Contain("ApplyNomadSliderChrome(_numCameras_slider.UnitySlider)"));
	}
}

public sealed class AddonHeaderLeaveSnapshotThemeTests {
	[Test]
	public void AddonHeader_SnapshotsIconLabelRectsAndRestoresViaBoundChrome() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SnapshotToolFaceLayout(iconRt)"));
		Assert.That(src, Does.Contain("SnapshotToolFaceLayout(labelRt)"));
		int restoreIx = src.IndexOf("static void RestoreHeaderButtonAuthoredChrome");
		Assert.That(restoreIx, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(restoreIx, System.Math.Min(900, src.Length - restoreIx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(button.transform)"));
		Assert.That(body, Does.Not.Contain("offsetMin = new Vector2(25f"),
			"Leave must not hardcode label offsets — use snapshotted layouts");
	}
}

public sealed class ColorsSlideoutGlyphThemeTests {
	[Test]
	public void ColorsSlideout_SkipsAuthoredIconFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "WorkflowRibbon_Colors_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
	}
}

public sealed class RestartWebuiEnsureThemeTests {
	[Test]
	public void RestartWebui_EnsuresHitFaceBeforeClearNonFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Webui", "RestartTheWebui.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemeTopStripButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(900, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(body.IndexOf("EnsureSelectableHitFace"), Is.LessThan(body.IndexOf("ClearNonFaceRaycastsForTheme")));
	}
}
