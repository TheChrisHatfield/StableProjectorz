using System.IO;
using NUnit.Framework;

public sealed class SkyboxBgCloneRtLeaveContractTests {
	[Test]
	public void OnDestroy_ReleasesCloneRenderTexture() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Icons", "SkyboxBackground_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void OnDestroy()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("_currentBG_texture_clone"));
		Assert.That(body, Does.Contain("DestroyImmediate(_currentBG_texture_clone)"));
	}
}

public sealed class MaskPainterStickyPaintingFlagContractTests {
	[Test]
	public void MissedLmbRelease_EndsStrokeWhenButtonUp() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "MaskPainter.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("void EndStroke()"));
		Assert.That(src, Does.Contain("if (_isPainting && !KeyMousePenInput.isLMBpressed())"));
		Assert.That(src, Does.Contain("EndStroke()"));
	}
}
