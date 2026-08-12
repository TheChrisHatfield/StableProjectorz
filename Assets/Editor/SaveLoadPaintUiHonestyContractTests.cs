using System.IO;
using NUnit.Framework;

public sealed class SaveLoadPaintUiHonestyContractTests {

	[Test]
	public void DoSaveProject_BlocksWhileLoading() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void DoSaveProject()", System.StringComparison.Ordinal);
		int j = src.IndexOf("public void DoLoadProject()", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("_isLoading"));
	}

	[Test]
	public void AddLayer_UsesNextLayerNumber() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Layers", "PaintLayerStack_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ConsumeNextDefaultLayerName()"));
		Assert.That(src, Does.Contain("name ?? ConsumeNextDefaultLayerName()"));
	}

	[Test]
	public void SetLayerOpacity_DoesNotFireOnLayersChanged() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Layers", "PaintLayerStack_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void SetLayerOpacity(", System.StringComparison.Ordinal);
		int j = src.IndexOf("void SetCompositeBlendSliceRange(", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Not.Contain("OnLayersChanged"));
	}

	[Test]
	public void MouseWorkbench_ClickOutside_CommitsColor() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "MouseWorkbench", "MouseWorkbench_Zone.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CommitAndClose()"));
		Assert.That(src, Does.Not.Contain("_colorPanel.Hide();"));
	}

	[Test]
	public void CollectPaintUI_FindsExistingStackBeforeCreate() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("FindObjectOfType<PaintLayerStack_MGR>(true)"));
	}
}
