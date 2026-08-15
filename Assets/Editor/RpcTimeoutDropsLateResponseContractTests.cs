using System.IO;
using NUnit.Framework;

public sealed class RpcTimeoutDropsLateResponseContractTests {

	[Test]
	public void ProcessRequest_AbandonsIdSoLatePublishIsDropped() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_abandonedResponseIds"));
		Assert.That(src, Does.Contain("TryPublishPendingResponse"));
		int processAt = src.IndexOf("JObject ProcessRequest(JObject request)", System.StringComparison.Ordinal);
		Assert.That(processAt, Is.GreaterThan(0));
		string processBody = src.Substring(processAt, System.Math.Min(3500, src.Length - processAt));
		Assert.That(processBody, Does.Contain("_abandonedResponseIds[id] = 0"));
		Assert.That(processBody, Does.Contain("Dropping late response for timed-out request"));
		Assert.That(processBody, Does.Contain("TryPublishPendingResponse(id, response)"));
		int coSaveAt = src.IndexOf("IEnumerator CoRespondWhenProjectSaveIdle", System.StringComparison.Ordinal);
		Assert.That(coSaveAt, Is.GreaterThan(0));
		string coSave = src.Substring(coSaveAt, System.Math.Min(4500, src.Length - coSaveAt));
		Assert.That(coSave, Does.Contain("TryPublishPendingResponse"));
		Assert.That(coSave, Does.Not.Contain("_pendingResponses[id] = new JObject"));
	}
}
