using System.IO;
using NUnit.Framework;

public sealed class PaintLayersDeleteDeferRebuildContractTests {

	[Test]
	public void LayersPanel_SchedulesRebuildOnLayersChanged_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ScheduleRebuildList"));
		Assert.That(src, Does.Contain("CoRebuildListSoon"));
		Assert.That(src, Does.Contain("OnLayersChanged += ScheduleRebuildList"));
		Assert.That(src, Does.Contain("DestroyImmediate so the click target is not freed"));
		Assert.That(src, Does.Contain("_rebuildListPendingWhileInactive"));
		Assert.That(src, Does.Contain("queue for OnEnable"));
	}
}
