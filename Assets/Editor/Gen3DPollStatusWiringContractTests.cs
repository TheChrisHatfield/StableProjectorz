using System.IO;
using NUnit.Framework;

public sealed class Gen3DPollStatusWiringContractTests {

	[Test]
	public void PollGenerationProgress_AssignsStatusFromPayload() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_API.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TryParseGen3DStatus"));
		int poll = src.IndexOf("IEnumerator PollGenerationProgress(", System.StringComparison.Ordinal);
		string body = src.Substring(poll, System.Math.Min(2200, src.Length - poll));
		Assert.That(body, Does.Contain("_generateStatus = polled"));
		Assert.That(body, Does.Contain("st == null"));
	}

	[Test]
	public void GenerateCrtn_HandlesPreviewReadyWithoutLeavingUiStuck() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_API.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("TaskStatus.PREVIEW_READY"));
		Assert.That(src, Does.Contain("Gen_downloadPreviews(callbacks)"));
		Assert.That(src, Does.Contain("Unexpected generation status"));
	}

	[Test]
	public void GenOnCancel_OnlyWhenGen3DBusy() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void Gen_OnCancel()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("isBusy"));
		Assert.That(body, Does.Contain("return;"));
	}
}
