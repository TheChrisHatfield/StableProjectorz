using System.IO;
using NUnit.Framework;

/// <summary>
/// FBX writer must not leave a stale on-disk file counting as a successful export.
/// </summary>
public sealed class SpzGoFbxSaveModelsContractTests {

	[Test]
	public void SaveModels_ReturnsBoolAndClearsPriorFile() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public bool SaveModels("),
			"SaveModels must return bool so callers can fail closed.");
		Assert.That(src, Does.Contain("File.Delete(finalFilepath_with_exten)"),
			"Prior FBX must be removed so a failed Initialize cannot look like success.");
		Assert.That(src, Does.Contain("return false;"),
			"Initialize/Export failures must return false.");
	}
}
