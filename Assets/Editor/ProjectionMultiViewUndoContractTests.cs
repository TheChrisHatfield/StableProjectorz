using System.IO;
using NUnit.Framework;

public sealed class ProjectionMultiViewUndoContractTests {

	[Test]
	public void MultiViewStroke_CapturesEveryPovBeforeApply() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "ProjectionsMasking", "Projections_MaskPainter.cs");
		string src = File.ReadAllText(path);
		int renderAt = src.IndexOf("OnRenderIntoCurrTex_please", System.StringComparison.Ordinal);
		Assert.That(renderAt, Is.GreaterThan(0));
		string body = src.Substring(renderAt, System.Math.Min(6000, src.Length - renderAt));
		Assert.That(body, Does.Contain("numPOV > 1"));
		Assert.That(body, Does.Contain("_multiViewUndoCapturesArmed"));
		Assert.That(body, Does.Contain("SchedulePreStrokeCapture"));
		int loopAt = body.IndexOf("for (int i = 0; i < mu._ObjectUV_brushedMaskR8.Count; i++)", System.StringComparison.Ordinal);
		int applyAt = body.IndexOf("Apply_into_MaskUtils", System.StringComparison.Ordinal);
		Assert.That(loopAt, Is.GreaterThan(0));
		Assert.That(applyAt, Is.GreaterThan(loopAt));
		Assert.That(body, Does.Contain("IsBusy"));
	}
}
