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
	public void NativeSpzGoExport_WaitsForSaveIdleBeforeStatusOk() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"CoSpzGoFinishExportWhenSaveIdle",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null,
			"Native Export must finish via CoSpzGoFinishExportWhenSaveIdle so status is not premature.");
	}
}
