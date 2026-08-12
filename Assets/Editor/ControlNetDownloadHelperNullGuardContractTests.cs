using System.IO;
using NUnit.Framework;

public sealed class ControlNetDownloadHelperNullGuardContractTests {

	[Test]
	public void OnRefreshWebuiInfo_NullGuardsDownloadHelper() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OnRefresh_WebuiInfo_Complete()", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(350, src.Length - i));
		Assert.That(body, Does.Contain("_downloadHelper?.OnRefreshInfoComplete"));
	}
}
