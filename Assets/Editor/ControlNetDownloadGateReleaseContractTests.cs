using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// The ControlNet "some unit is downloading" gate must reopen when a download never starts.
/// StableDiffusion_Hub refuses to generate while isSomeUnit_downloadingModels is true, and every unit
/// sets _contentsCanvGroup.interactable = false on the started event, so a stuck gate freezes the
/// ControlNet panels AND blocks Gen Art until restart. A refused start reports progress 0, which is
/// indistinguishable from "0% downloaded" — hence the explicit bool handoff pinned here.
/// </summary>
public sealed class ControlNetDownloadGateReleaseContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing source: {path}");
		return File.ReadAllText(path);
	}

	static string DownloadHelperSrc() => Read("Assets", "_gm", "Features", "StableDiffusion",
		"Controlnet", "ControlNetUnit_DownloadHelper.cs");

	[Test]
	public void DownloadFile_ReportsWhetherItActuallyStarted() {
		string src = Read("Assets", "_gm", "_Core", "IO", "Download", "DownloadFile_if_NotYetExist.cs");

		Assert.That(src, Does.Contain("public bool DownloadFile("),
			"callers gate UI on this call, so it must report a refused start");
		Assert.That(src, Does.Contain("TryResolveControlNetModelsDir"));

		int deny = src.IndexOf("onProgress?.Invoke(0f);", StringComparison.Ordinal);
		Assert.That(deny, Is.GreaterThan(0), "the deny path must still report progress to listeners");
		string afterDeny = src.Substring(deny, Math.Min(80, src.Length - deny));
		Assert.That(afterDeny, Does.Contain("return false;"),
			"an unresolved models dir must return false, not merely report 0%");

		Assert.That(src, Does.Contain("Download_MGR.instance == null"),
			"a missing Download_MGR must be refused instead of throwing past the caller's gate reset");
		Assert.That(src, Does.Contain("return true;"), "the handoff path must report success");
	}

	[Test]
	public void RefusedStart_ReopensTheGateAndRestoresUnits() {
		string src = DownloadHelperSrc();

		int i = src.IndexOf("void OnDownload_MandatoryDepthModel_button()", StringComparison.Ordinal);
		int end = src.IndexOf("void AbortDownloadGate(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		Assert.That(end, Is.GreaterThan(i), "the abort helper must follow the button handler");
		string body = src.Substring(i, end - i);

		Assert.That(body, Does.Contain("if (!_downloadModel_ifNotExist.DownloadFile(\"\", \"\", onProgress))"),
			"the button must branch on whether the download actually started");
		Assert.That(body, Does.Contain("AbortDownloadGate("),
			"both refused-start paths must go through the gate release");

		// The missing-helper path used to clear the flag but never re-enable the units it had disabled.
		int nullHelper = body.IndexOf("_downloadModel_ifNotExist == null", StringComparison.Ordinal);
		Assert.That(nullHelper, Is.GreaterThan(0));
		string nullBranch = body.Substring(nullHelper, Math.Min(260, body.Length - nullHelper));
		Assert.That(nullBranch, Does.Contain("AbortDownloadGate("),
			"clearing the flag alone leaves every unit non-interactable");
	}

	[Test]
	public void AbortHelper_ClearsFlagAndSignalsFailure() {
		string src = DownloadHelperSrc();
		int i = src.IndexOf("void AbortDownloadGate(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(500, src.Length - i));

		Assert.That(body, Does.Contain("isSomeUnit_downloadingModels = false;"),
			"the shared gate must reopen or Gen Art stays blocked");
		Assert.That(body, Does.Contain("_onSomeUnit_stoppedDownloadModel?.Invoke(this, false)"),
			"listeners must be told the model was NOT installed");
	}

	[Test]
	public void StoppedEvent_CarriesSuccessSoNoFalseRestartNotice() {
		string src = DownloadHelperSrc();

		Assert.That(src, Does.Contain("Action<ControlNetUnit_DownloadHelper, bool> _onSomeUnit_stoppedDownloadModel"),
			"the stopped signal must distinguish completion from abort");
		Assert.That(src, Does.Contain("_onSomeUnit_stoppedDownloadModel?.Invoke(this, true)"),
			"the completed download must report success");

		int handler = src.IndexOf("void OnSomeUnit_StopDownloadModel(", StringComparison.Ordinal);
		Assert.That(handler, Is.GreaterThan(0));
		string body = src.Substring(handler, Math.Min(600, src.Length - handler));
		Assert.That(body, Does.Contain("_contentsCanvGroup.interactable = true;"),
			"units must regain interactivity on both outcomes");
		Assert.That(body, Does.Contain("if (didDownload)"),
			"the 'please restart StableProjectorz' notice must not appear when nothing downloaded");
		Assert.That(body, Does.Contain("SetActive(!didDownload)"),
			"a failed start must leave the download CTA visible so the user can retry");
	}

	[Test]
	public void FailedNetworkDownload_DoesNotReportProgressOneAsSuccess() {
		string mgr = Read("Assets", "_gm", "_Core", "IO", "Download", "Download_MGR.cs");
		Assert.That(mgr, Does.Contain("onProgress?.Invoke(-1f)"),
			"network failure must not look like a completed download to gate owners");
		Assert.That(mgr, Does.Contain("onProgress?.Invoke(1.0f)"),
			"success still reports 1.0 after bytes land");

		// Failure invoke must not share the success branch's unconditional 1.0.
		int fail = mgr.IndexOf("Downloading failed:", StringComparison.Ordinal);
		Assert.That(fail, Is.GreaterThan(0));
		string failWindow = mgr.Substring(fail, Math.Min(700, mgr.Length - fail));
		Assert.That(failWindow, Does.Contain("Invoke(-1f)"));
		Assert.That(failWindow, Does.Not.Contain("Invoke(1.0f)"),
			"the failure branch must not also fire the success completion progress");
	}

	[Test]
	public void ProgressNegative_ReopensGateAsFailure() {
		string src = DownloadHelperSrc();
		Assert.That(src, Does.Contain("if (pcnt01 < 0f)"),
			"ControlNet must treat Download_MGR failure sentinel as abort, not success");
		Assert.That(src, Does.Contain("AbortDownloadGate(\"ControlNet download failed"),
			"failed download must reopen the gate and not claim restart-SPZ");
	}

	[Test]
	public void DestroyWhileDownloading_ReleasesGateOwnedByThisUnit() {
		string src = DownloadHelperSrc();
		Assert.That(src, Does.Contain("_downloadGateOwner"),
			"only the owner may clear the shared Gen Art gate");
		Assert.That(src, Does.Contain("ReferenceEquals(_downloadGateOwner, this)"),
			"peer unit destroy must not clear another unit's in-flight download");
		int destroy = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(destroy, Is.GreaterThan(0));
		string body = src.Substring(destroy, Math.Min(500, src.Length - destroy));
		Assert.That(body, Does.Contain("AbortDownloadGate("),
			"owner destroy must reopen the gate before Hub permanently blocks Gen Art");
	}

	[Test]
	public void OtherSubscriberMatchesTheStoppedSignature() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion", "Controlnet",
			"ControlNetUnit_Dropdowns.cs");
		Assert.That(src, Does.Contain("OnSomeUnit_StopDownloadModel(ControlNetUnit_DownloadHelper who, bool didDownload)"),
			"the dropdown restores itself on either outcome, but must match the delegate");
	}
}
