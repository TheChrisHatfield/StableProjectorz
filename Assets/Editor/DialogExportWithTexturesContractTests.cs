using NUnit.Framework;
using spz;

/// <summary>
/// Contract: dialog export must not treat "opened dialog" as mesh-written success path.
/// FastPath returns the bool from Save_MGR (busy/null → false).
/// </summary>
public sealed class DialogExportWithTexturesContractTests {

	[Test]
	public void DefersResponseUntilProjectSaveIdle_StillOnlyToPath() {
		// Dialog export can wait on the user; keep idle-wait on the non-interactive path only.
		Assert.That(
			Addon_SocketServer.DefersResponseUntilProjectSaveIdle("spz.cmd.export_3d_with_textures_to_path"),
			Is.True);
		Assert.That(
			Addon_SocketServer.DefersResponseUntilProjectSaveIdle("spz.cmd.export_3d_with_textures"),
			Is.False);
	}
}
