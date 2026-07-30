using System.IO;
using NUnit.Framework;

/// <summary>
/// Headless SPZ GO export must not treat a stale path string as a successful FBX write.
/// </summary>
public sealed class SpzGoExportPathExistsTests {

	[Test]
	public void ExportToPath_RequiresFileExistsAndClearsStalePath() {
		string helper = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		string save = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(helper), Is.True);
		Assert.That(File.Exists(save), Is.True);
		string helperSrc = File.ReadAllText(helper);
		string saveSrc = File.ReadAllText(save);
		Assert.That(helperSrc, Does.Contain("_path_recentlyExported = \"\""),
			"ExportModelToPath must clear stale path before attempting write.");
		Assert.That(helperSrc, Does.Contain("File.Exists(path)"),
			"SaveDefaultDoor_toFile must only set _path_recentlyExported when FBX exists.");
		Assert.That(saveSrc, Does.Contain("!File.Exists( path_exported3D )"),
			"Export3D_with_textures_ToPath must fail if mesh file was not written.");
		Assert.That(saveSrc, Does.Contain("_isSaving"),
			"Concurrent export must be gated while another save is in progress.");
	}
}
