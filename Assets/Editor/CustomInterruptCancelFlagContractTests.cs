using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Custom-workflow interrupt (HDR spheres) must not leave _cancelRequested sticky with no
/// failsafe — that can abort the next Gen Art prep before Generate_* clears the flag.
/// </summary>
public sealed class CustomInterruptCancelFlagContractTests {

	[Test]
	public void RequestHttpInterruptOnly_ClearsCancelOnSettleAndStuckTimer() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void RequestHttpInterruptOnly()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("IEnumerator Generate_txt2Img_crtn", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("Send_StopGenerateRequest(() =>"));
		Assert.That(body, Does.Contain("_cancelRequested = false"));
		Assert.That(body, Does.Contain("ClearCancelFlagIfStuck"));
		Assert.That(body, Does.Not.Contain("OnFinishTheInterrupt()"),
			"custom interrupt must not double-fire Gen Art finished UI");
	}

	[Test]
	public void MarkCustomWorkflow_Done_ClearsCancelFlag() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void MarkCustomWorkflow_Done()", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(350, src.Length - i));
		Assert.That(body, Does.Contain("_cancelRequested = false"));
	}
}
