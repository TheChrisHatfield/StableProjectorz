using System.IO;
using NUnit.Framework;

public sealed class RpcSaveLoadFilepathHonorContractTests {

	[Test]
	public void SocketSaveLoad_PassFilepathIntoFastPath() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		int saveAt = src.IndexOf("result[\"success\"] = fastPath.SaveProject(@params?[\"filepath\"]", System.StringComparison.Ordinal);
		Assert.That(saveAt, Is.GreaterThan(0), "RPC save_project must pass filepath into FastPath.");
		int loadAt = src.IndexOf("result[\"success\"] = fastPath.LoadProject(@params?[\"filepath\"]", System.StringComparison.Ordinal);
		Assert.That(loadAt, Is.GreaterThan(0), "RPC load_project must pass filepath into FastPath.");
	}

	[Test]
	public void Helper_ExposesHeadlessLoadAndSavePath() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("LoadProjectFromPath"));
		Assert.That(src, Does.Contain("SaveProject( string forcedFilepath"));
		Assert.That(src, Does.Contain("ApplyProjectFile"));
	}
}
