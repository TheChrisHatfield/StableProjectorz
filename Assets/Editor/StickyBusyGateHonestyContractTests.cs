using System.IO;
using NUnit.Framework;

/// <summary>
/// Sticky busy gates: MergeIcons _isSaving, AO isGeneratingAO, UDIM task counter, save _Data restore on throw.
/// </summary>
public sealed class StickyBusyGateHonestyContractTests {

	[Test]
	public void MergeIcons_OnHaveAlbedo_ClearsIsSavingInFinally_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void OnHaveAlbedo(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(1100, src.Length - i));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("_isSaving = false"));
		Assert.That(body, Does.Contain("mgr == null"));
	}

	[Test]
	public void BakeAO_FinallyClearsIsGeneratingAO_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "AO", "AmbientOcclusion_Baker.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator BakeAO_crtn", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("void BakeOA_Preliminaries", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("CompleteBake_and_Cleanup()"));
		Assert.That(body, Does.Contain("onBakeComplete?.Invoke(ok)"));
	}

	[Test]
	public void UdimsLaunchTask_DecrementsOnFaultOrNullMesh_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "TextureTools", "UDIMs", "UDIMs_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void LaunchTask(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(1200, src.Length - i));
		Assert.That(body, Does.Contain("mesh3d._sharedMesh == null"));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("Interlocked.Decrement"));
		Assert.That(body, Does.Contain("t.IsFaulted"));
	}

	[Test]
	public void SaveProj_RestoresDataDirOnGatherException_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("gatherEx"));
		Assert.That(src, Does.Contain("CommitOrRestoreDataDir(spz.filepath_dataDir, false)"));
		Assert.That(src, Does.Contain("No yield inside try/catch"));
	}

	[Test]
	public void Gen3dBrushDirection_NullGuardsBackgroundPainter_Source() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BrushRibbon_UI", "Gen3D_BrushRibbon_UI_Direction.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Background_Painter.instance != null"));
		Assert.That(src, Does.Contain("Cursor_UI.instance?.SetCursorColor"));
	}
}
