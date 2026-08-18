using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Cancel during SD prep must stop the request coroutine and clear _finalPreparations_beforeGen
/// so DenyWithMessage cannot stick and a late POST cannot fire after interrupt.
/// </summary>
public sealed class SdCancelStopsPrepContractTests {

	[Test]
	public void OnStopGenerate_StopsActiveRequestAndClearsPrepFlag() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_GenRequests_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_activeRequestCrtn"));
		Assert.That(src, Does.Contain("_cancelRequested"));
		Assert.That(src, Does.Contain("AbortPrepAfterCancel"));
		int stop = src.IndexOf("public void OnStopGenerate_Button()", System.StringComparison.Ordinal);
		int rerender = src.IndexOf("IEnumerator ReRenderAgainAfterFrames", stop, System.StringComparison.Ordinal);
		string stopBody = src.Substring(stop, rerender - stop);
		Assert.That(stopBody, Does.Contain("StopCoroutine(_activeRequestCrtn)"));
		Assert.That(stopBody, Does.Contain("AbortPrepAfterCancel"));
		Assert.That(stopBody, Does.Contain("prepOnlyAbort"),
			"Prep-only cancel must not arm FinishTheInterrupt_ifStuck (kills next gen).");
		Assert.That(stopBody, Does.Contain("ClearStuckInterruptTimer"));
		int interruptAt = stopBody.IndexOf("Send_StopGenerateRequest", StringComparison.Ordinal);
		Assert.That(interruptAt, Is.GreaterThan(0));
		Assert.That(stopBody.Substring(interruptAt), Does.Not.Contain("OnConfirmed_FinishedGenerate(canceled"),
			"Mid-flight cancel must not finish UI before interrupt settles");
		Assert.That(src, Does.Contain("ClearStuckInterruptTimer()"),
			"New Generate_* must cancel any pending stuck-interrupt timer.");
		int finish = src.IndexOf("void OnFinishTheInterrupt()", System.StringComparison.Ordinal);
		string finishBody = src.Substring(finish, Math.Min(1100, src.Length - finish));
		Assert.That(finishBody, Does.Contain("_finalPreparations_beforeGen = false"));
		Assert.That(finishBody, Does.Contain("if (!_cancelRequested)"),
			"Stuck timer / interrupt callback must not tear down a live generation or double-finish.");
		Assert.That(finishBody, Does.Contain("OnConfirmed_FinishedGenerate(canceled:true)"),
			"UI busy must clear only when interrupt cleanup runs — not when Cancel is first pressed.");
	}

	[Test]
	public void StopGenerate_AbortsInFlightHttpAndDoesNotReuseGenerateCallback() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_NetworkSender.cs");
		string src = File.ReadAllText(path);
		int stop = src.IndexOf("public void Send_StopGenerateRequest", StringComparison.Ordinal);
		Assert.That(stop, Is.GreaterThan(0));
		int next = src.IndexOf("IEnumerator Send_GenerateRequest_crtn", stop, StringComparison.Ordinal);
		string body = src.Substring(stop, next - stop);
		Assert.That(body, Does.Contain("AbortActiveRequest"),
			"Cancel must Abort the live generate POST, not only StopAllCoroutines");
		Assert.That(body, Does.Contain("_onCompleted = null"),
			"/interrupt must not invoke the generate OnCompleted handler");
		Assert.That(body, Does.Contain("SendInterruptRequest_crtn"),
			"interrupt needs its own path so settle can finish UI without parsing as txt2img");
	}
}
