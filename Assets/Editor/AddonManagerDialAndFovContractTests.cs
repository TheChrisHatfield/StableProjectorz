using System.IO;
using NUnit.Framework;

public sealed class AddonManagerDialStatusHonestyContractTests {

	[Test]
	public void DialStatus_BranchesOnRememberPreference() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("KeepNextLaunchHint()"));
		Assert.That(src, Does.Contain("enable restore is off (Remember)"));
		Assert.That(src, Does.Not.Contain("Click Save settings to keep next launch.\""),
			"Hardcoded keep-next-launch copy must go through KeepNextLaunchHint.");
	}
}

public sealed class FastPathViewCameraFovNullContractTests {

	[Test]
	public void ProjectionRpc_NullChecksFovMgr() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		string src = File.ReadAllText(path);
		int get = src.IndexOf("public JObject GetViewCameraProjectionJson(", System.StringComparison.Ordinal);
		int set = src.IndexOf("public bool SetViewCameraProjectionRpc(", System.StringComparison.Ordinal);
		int next = src.IndexOf("// ============================================", set, System.StringComparison.Ordinal);
		string getBody = src.Substring(get, set - get);
		string setBody = src.Substring(set, next - set);
		Assert.That(getBody, Does.Contain("vc.fovMgr != null"));
		Assert.That(setBody, Does.Contain("vc.fovMgr != null"));
		Assert.That(setBody, Does.Contain("cam.fieldOfView = f"));
	}
}
