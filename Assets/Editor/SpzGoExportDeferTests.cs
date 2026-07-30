using System.Reflection;
using NUnit.Framework;
using spz;

/// <summary>
/// Guards SPZ GO headless export: success must wait for texture write (Save_MGR idle)
/// on both TCP RPC and native in-app button paths.
/// </summary>
public sealed class SpzGoExportDeferTests {

	[Test]
	public void Export3DToPath_DefersResponseUntilProjectSaveIdle() {
		Assert.That(
			Addon_SocketServer.DefersResponseUntilProjectSaveIdle("spz.cmd.export_3d_with_textures_to_path"),
			Is.True,
			"Blender/SPZ GO must not see success before textures finish writing.");
	}

	[Test]
	public void OtherCommands_DoNotDeferOnSaveIdle() {
		Assert.That(Addon_SocketServer.DefersResponseUntilProjectSaveIdle("spz.cmd.import_3d_model"), Is.False);
		Assert.That(Addon_SocketServer.DefersResponseUntilProjectSaveIdle("spz.cmd.get_project_data_dir"), Is.False);
		Assert.That(Addon_SocketServer.DefersResponseUntilProjectSaveIdle(null), Is.False);
		Assert.That(Addon_SocketServer.DefersResponseUntilProjectSaveIdle(""), Is.False);
	}

	[Test]
	public void Import3DModel_DefersResponseUntilImportIdle() {
		Assert.That(
			Addon_SocketServer.DefersResponseUntilImportIdle("spz.cmd.import_3d_model"),
			Is.True,
			"Blender→SPZ import must not report success before Assimp/UDIM finishes.");
		Assert.That(Addon_SocketServer.DefersResponseUntilImportIdle("spz.cmd.export_3d_with_textures_to_path"), Is.False);
	}

	[Test]
	public void NativeSpzGoImport_WaitsForImportIdleBeforeStatusOk() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"CoSpzGoFinishImportWhenIdle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null,
			"Native Import must finish via CoSpzGoFinishImportWhenIdle so status is not premature.");
	}

	[Test]
	public void NativeSpzGoExport_WaitsForSaveIdleBeforeStatusOk() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"CoSpzGoFinishExportWhenSaveIdle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null,
			"Native Export must finish via CoSpzGoFinishExportWhenSaveIdle so status is not premature.");
		string path = System.IO.Path.Combine(
			System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("sm != null && !sm._isSaving"),
			"Missing Save_MGR must not count as Export OK.");
		Assert.That(src, Does.Not.Contain("sm == null || !sm._isSaving"),
			"Do not treat null Save_MGR as successful texture write.");
	}

	[Test]
	public void TcpDefer_FailsWhenSaveMgrLostMidWrite() {
		string path = System.IO.Path.Combine(
			System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = System.IO.File.ReadAllText(path);
		Assert.That(src, Does.Contain("Save_MGR unavailable during texture write"),
			"TCP export defer must fail closed if Save_MGR disappears mid-write.");
	}
}
