using System.IO;
using NUnit.Framework;

public sealed class MaskPainterViewportNullGuardContractTests {

	[Test]
	public void OnUpdate_NullGuardsMainViewportHover() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "MaskPainter.cs");
		string src = File.ReadAllText(path);
		int updateAt = src.IndexOf("void OnUpdate()", System.StringComparison.Ordinal);
		Assert.That(updateAt, Is.GreaterThan(0));
		string body = src.Substring(updateAt, System.Math.Min(500, src.Length - updateAt));
		Assert.That(body, Does.Contain("MainViewport_UI.instance?.isCursorHoveringMe()"));
	}
}
