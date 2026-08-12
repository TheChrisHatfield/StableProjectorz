using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ControlNetDownloadHelperLeaveTests {
	[Test]
	public void DownloadHelper_ThemesAndRestoresMandatoryDepthButton_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_DownloadHelper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_download_mandatoryDepthModel.transform)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_download_mandatoryDepthModel)"));
		Assert.That(src, Does.Contain("ApplyDownloadMoreSlideChrome"));
	}
}
