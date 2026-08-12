using System.IO;
using NUnit.Framework;

public sealed class RpcSaveLoadDeferHonestyContractTests {

	[Test]
	public void DefersResponse_IncludesSaveAndLoadProject() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public static bool DefersResponseUntilProjectSaveIdle(", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("spz.cmd.save_project"));
		Assert.That(body, Does.Contain("spz.cmd.load_project"));
	}

	[Test]
	public void CoRespond_UsesLastProjectSaveLoadSucceeded() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("LastProjectSaveSucceeded"));
		Assert.That(src, Does.Contain("LastProjectLoadSucceeded"));
		Assert.That(src, Does.Contain("save cancelled or failed"));
		Assert.That(src, Does.Contain("load cancelled or failed"));
	}
}
