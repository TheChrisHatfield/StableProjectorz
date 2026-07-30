using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AuthoredIconFaceSolidSquareThemeTests {

	[Test]
	public void SpzUiThemeOps_DefinesIsAuthoredIconFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public static bool IsAuthoredIconFace"));
		Assert.That(src, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
		Assert.That(src, Does.Contain("IsAuthoredIconFace(button.targetGraphic)"));
	}

	[Test]
	public void RoleMatrix_SkipsAuthoredIconFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeRoleMatrix.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
	}
}

public sealed class UpscalerSoftDisableDeferThemeTests {

	[Test]
	public void Upscalers_DeferSoftDisableOneFrameAfterThemeChanged() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Upscalers_MainPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CoReapplySoftDisableNextFrame"));
		Assert.That(src, Does.Contain("yield return null"));
	}
}

public sealed class CollectLayersDualOwnershipThemeTests {

	[Test]
	public void PaintCollect_SkipsLayersPanelSubtree() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("GetComponentInParent<PaintTab_LayersPanel_UI>(true)"));
	}
}

public sealed class CnDownloadSlideSingleOwnerThemeTests {

	[Test]
	public void ControlNetUnit_SkipsDownloadSlidesInRoleMatrix() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SkipDownloadSlides = true"));
		Assert.That(src, Does.Contain("_downloadHelper?.ApplyThemeTokens()"));
	}
}
