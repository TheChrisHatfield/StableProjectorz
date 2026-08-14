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

	[Test]
	public void ApplyColorLayer_KeepsCompositingLayerStackWhileSaving_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Inpaint", "Inpaint_MaskPainter.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void ApplyColorLayer_To_UV_Textures(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("hasLayerStack"),
			"Must detect layer stack before the save early-out.");
		Assert.That(body, Does.Contain("!hasLayerStack && Save_MGR.instance != null && Save_MGR.instance._isSaving"),
			"Save dialog must not blank mesh paint when a layer stack exists.");
	}
}
