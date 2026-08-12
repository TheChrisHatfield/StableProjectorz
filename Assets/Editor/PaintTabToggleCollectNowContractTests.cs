using System.IO;
using NUnit.Framework;

public sealed class PaintTabToggleCollectNowContractTests {

	[Test]
	public void OnPaintToggle_CallsCollectNow() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void On_Paint_Toggle(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("CollectNow()"),
			"Paint tab toggle must re-bind collectors when panel already active.");
	}

	[Test]
	public void LayersPanel_Start_FindsInactiveLayerStack() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("FindObjectOfType<PaintLayerStack_MGR>(true)"));
	}
}
