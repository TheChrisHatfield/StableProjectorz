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
		Assert.That(src, Does.Contain("ClearStuckInterruptTimer()"),
			"New Generate_* must cancel any pending stuck-interrupt timer.");
		int finish = src.IndexOf("void OnFinishTheInterrupt()", System.StringComparison.Ordinal);
		string finishBody = src.Substring(finish, Math.Min(900, src.Length - finish));
		Assert.That(finishBody, Does.Contain("_finalPreparations_beforeGen = false"));
		Assert.That(finishBody, Does.Contain("!_cancelRequested && _isGeneratingWhat"),
			"Stuck timer must not tear down a live generation.");
	}
}
