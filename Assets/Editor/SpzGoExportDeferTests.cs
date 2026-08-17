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
		Assert.That(src, Does.Contain("saveIdle && stampOk"),
			"Export OK for auto-import must require Save_MGR idle and .spz_go_ready stamp.");
		Assert.That(src, Does.Contain(".spz_go_ready"),
			"Native export finish must verify Blender auto-import ready stamp.");
		Assert.That(src, Does.Contain("_path_recentlyExported"),
			"Stamp check must use the FBX path actually written, not only the panel path.");
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
		Assert.That(src, Does.Contain(".spz_go_ready"),
			"TCP export defer must require Blender auto-import ready stamp.");
		Assert.That(src, Does.Contain("ready stamp missing"),
			"TCP export must fail closed when .spz_go_ready is absent after save idle.");
		Assert.That(src, Does.Contain("_path_recentlyExported"),
			"TCP stamp check must prefer the FBX path actually written.");
		Assert.That(src, Does.Contain("!string.IsNullOrEmpty(meshFilePath)"),
			"Ready stamp is ToPath-only; dialog export must not require .spz_go_ready.");
		Assert.That(src, Does.Contain("export cancelled or mesh not written"),
			"Dialog export must fail when cancel leaves no written mesh.");
		Assert.That(src, Does.Contain("export_projection_textures"),
			"Texture-only projection export must share the deferred idle path.");
		int texOnlyAt = src.IndexOf(
			"Texture-only exports never write an FBX", System.StringComparison.Ordinal);
		int meshRequireAt = src.IndexOf(
			"export cancelled or mesh not written", System.StringComparison.Ordinal);
		Assert.That(texOnlyAt, Is.GreaterThan(0),
			"Projection/view exports must not reuse the dialog mesh-written check.");
		Assert.That(meshRequireAt, Is.GreaterThan(texOnlyAt));
	}

	[Test]
	public void HttpExportToPath_FailsClosedWhenReadyStampMissing() {
		// TCP already fails closed on a missing .spz_go_ready after save idle. HTTP must match, or
		// ZBrush/Painter/Blender bridges get success:true and load a mesh SPZ refused to mark ready.
		string path = System.IO.Path.Combine(
			System.IO.Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_HttpServer.cs");
		string src = System.IO.File.ReadAllText(path);
		int i = src.IndexOf("case \"3d_to_path\"", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int j = src.IndexOf("case \"projection_textures\"", i, System.StringComparison.Ordinal);
		Assert.That(j, Is.GreaterThan(i));
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("WaitForProjectSaveIdle_offMainThread"),
			"HTTP must wait for texture write like TCP");
		Assert.That(body, Does.Contain("SpzGoExchangeReadyStampExists"),
			"HTTP must require the ready stamp after idle — not return the original started success");
		Assert.That(body, Does.Contain("ready stamp missing"),
			"failure must say why auto-import must not run");
	}
}
