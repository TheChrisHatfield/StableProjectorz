using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class UpscalersSoftDisableAlphaLeaveTests {
	[Test]
	public void ApplySoftAlpha_LeaveResnapshotsFullAuthoredAlpha_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Upscalers_MainPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ResnapshotAuthoredGraphicColor(face)"));
		Assert.That(src, Does.Contain("soft-dim must not become the Restore baseline"));
		int idx = src.IndexOf("static void ApplySoftAlpha", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("!SpzUiThemeOps.ShouldRecolorBoundChrome"));
		Assert.That(body, Does.Contain("restored.a = 1f"));
	}
}
