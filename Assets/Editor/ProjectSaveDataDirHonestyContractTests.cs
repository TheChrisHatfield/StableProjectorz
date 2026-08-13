using System.IO;
using NUnit.Framework;

/// <summary>
/// Project save must not wipe _Data / run composite when JSON write failed.
/// </summary>
public sealed class ProjectSaveDataDirHonestyContractTests {

	[Test]
	public void CreateDataDir_Source_StagesBackupInsteadOfWipeFirst() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("__spz_bak"));
		Assert.That(src, Does.Contain("Directory.Move(newDirectoryPath, bak)"));
		Assert.That(src, Does.Contain("CommitOrRestoreDataDir"));
	}

	[Test]
	public void SaveProj_Source_SkipsCompositeAndLastPathWhenJsonFails() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("IEnumerator SaveProj_crtn", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("IEnumerator Save_FinalCompositeTexture_crtn", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("CommitOrRestoreDataDir(spz.filepath_dataDir, LastProjectSaveSucceeded)"));
		Assert.That(body, Does.Contain("if (!LastProjectSaveSucceeded)"));
		Assert.That(body, Does.Contain("yield break"));
		int fail = body.IndexOf("if (!LastProjectSaveSucceeded)", System.StringComparison.Ordinal);
		int lastPath = body.IndexOf("_last_saveFilepath = saveFile", System.StringComparison.Ordinal);
		Assert.That(fail, Is.GreaterThanOrEqualTo(0));
		Assert.That(lastPath, Is.GreaterThan(fail),
			"last path must only update after successful JSON (after the fail early-out)");
	}
}
