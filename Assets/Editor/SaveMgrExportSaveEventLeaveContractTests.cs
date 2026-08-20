using System.IO;
using NUnit.Framework;

/// <summary>
/// Save_MGR wires ExportSave_UI_MGR static Save/Load/Export buttons in Start. An anonymous
/// export lambda cannot be unsubscribed; without OnDestroy leave, reload keeps dead handlers.
/// </summary>
public sealed class SaveMgrExportSaveEventLeaveContractTests {

	static string ReadSrc() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void ExportSaveSubscriptions_UseNamedHandlersAndOnDestroyLeave() {
		string src = ReadSrc();
		Assert.That(src, Does.Contain("OnSaveProject_Button += DoSaveProject"));
		Assert.That(src, Does.Contain("OnLoadProject_Button += DoLoadProject"));
		Assert.That(src, Does.Contain("OnExport3D_Button += OnExport3D_Button"),
			"named handler required so leave can -= the same delegate");
		Assert.That(src, Does.Not.Contain("OnExport3D_Button += ()"),
			"anonymous lambdas cannot be removed from the static bus");

		Assert.That(src, Does.Contain("void OnDestroy()"));
		Assert.That(src, Does.Contain("OnSaveProject_Button -= DoSaveProject"));
		Assert.That(src, Does.Contain("OnLoadProject_Button -= DoLoadProject"));
		Assert.That(src, Does.Contain("OnExport3D_Button -= OnExport3D_Button"));
		Assert.That(src, Does.Contain("instance = null"));
	}
}
