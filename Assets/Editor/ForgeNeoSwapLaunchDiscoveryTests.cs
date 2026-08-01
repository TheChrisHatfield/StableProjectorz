using System.IO;
using NUnit.Framework;
using spz;

/// <summary>
/// forge-neo-swap Phase A: launch discovery accepts reForge Neo + classic Forge (Neo preferred).
/// </summary>
public sealed class ForgeNeoSwapLaunchDiscoveryTests {

	[Test]
	public void CandidateFolderNames_NeoPreferredThenClassic() {
		Assert.That(LaunchWebUIBatFile.WebuiCandidateFolderNames.Length, Is.GreaterThanOrEqualTo(2));
		Assert.That(LaunchWebUIBatFile.WebuiCandidateFolderNames[0],
			Is.EqualTo(LaunchWebUIBatFile.WebuiFolderNameNeo));
		Assert.That(LaunchWebUIBatFile.WebuiCandidateFolderNames[1],
			Is.EqualTo(LaunchWebUIBatFile.WebuiFolderName));
		Assert.That(LaunchWebUIBatFile.WebuiFolderNameNeo, Is.EqualTo("stable-diffusion-webui-reForge"));
		Assert.That(LaunchWebUIBatFile.WebuiFolderName, Is.EqualTo("stable-diffusion-webui-forge"));
	}

	[Test]
	public void GetCandidateWebuiDirsUnder_OrdersNeoFirst() {
		string parent = Path.Combine(Path.GetTempPath(), "spz_forge_neo_candidates");
		string[] dirs = LaunchWebUIBatFile.GetCandidateWebuiDirsUnder(parent);
		Assert.That(dirs[0], Does.EndWith(LaunchWebUIBatFile.WebuiFolderNameNeo));
		Assert.That(dirs[1], Does.EndWith(LaunchWebUIBatFile.WebuiFolderName));
	}

	[Test]
	public void TryResolveLaunchFileUnderParent_PrefersNeoWhenBothHaveBat() {
		string root = Path.Combine(Path.GetTempPath(), "spz_forge_neo_pref_" + System.Guid.NewGuid().ToString("N"));
		string neo = Path.Combine(root, LaunchWebUIBatFile.WebuiFolderNameNeo);
		string classic = Path.Combine(root, LaunchWebUIBatFile.WebuiFolderName);
		Directory.CreateDirectory(neo);
		Directory.CreateDirectory(classic);
		string neoBat = Path.Combine(neo, "run_noQuickEdit.bat");
		string classicBat = Path.Combine(classic, "run_noQuickEdit.bat");
		File.WriteAllText(neoBat, "@echo off\r\n");
		File.WriteAllText(classicBat, "@echo off\r\n");
		try {
			string found = LaunchWebUIBatFile.TryResolveLaunchFileUnderParent(root);
			Assert.That(found, Is.EqualTo(Path.GetFullPath(neoBat)));
		} finally {
			try { Directory.Delete(root, true); } catch { /* ignore */ }
		}
	}

	[Test]
	public void TryResolveLaunchFileUnderParent_FallsBackToClassicWhenNeoStub() {
		string root = Path.Combine(Path.GetTempPath(), "spz_forge_neo_stub_" + System.Guid.NewGuid().ToString("N"));
		string neo = Path.Combine(root, LaunchWebUIBatFile.WebuiFolderNameNeo);
		string classic = Path.Combine(root, LaunchWebUIBatFile.WebuiFolderName);
		Directory.CreateDirectory(neo); // stub: no bat
		Directory.CreateDirectory(classic);
		string classicBat = Path.Combine(classic, "run.bat");
		File.WriteAllText(classicBat, "@echo off\r\n");
		try {
			string found = LaunchWebUIBatFile.TryResolveLaunchFileUnderParent(root);
			Assert.That(found, Is.EqualTo(Path.GetFullPath(classicBat)));
		} finally {
			try { Directory.Delete(root, true); } catch { /* ignore */ }
		}
	}

	[Test]
	public void BuildForTesting_PreservesNeoAndClassicOnClean() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "Editor", "BuildForTesting.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("WebuiForgeNeoFolderName"));
		Assert.That(src, Does.Contain("stable-diffusion-webui-reForge"));
		Assert.That(src, Does.Contain("WebuiPreserveFolderNames"));
	}

	[Test]
	public void LaunchSearch_UsesCandidateFolderArray() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Webui", "Launch_WebUI_bat_File.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("WebuiCandidateFolderNames"));
		Assert.That(src, Does.Contain("TryPickLaunchAmongCandidateFolders"));
		Assert.That(src, Does.Contain("TryResolveLaunchFileUnderParent"));
	}
}
