using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Two "work started but cleanup unreachable" bugs:
/// - ModelsHandler3D_ImportHelper set _isImportingModel before announcing the start and launching the
///   import coroutine. A throwing start listener (it removes the current model) or StartCoroutine on an
///   inactive helper meant no coroutine ever ran to clear the flag, so CanImportFile,
///   ImportModel_via_Filepath and project TryLoad all refused "already importing" until restart.
/// - Screenshot_MGR allocated a screen-sized RT plus a crop and destroyed them on the coroutine's last
///   line, but CancelPendingScriptCapture kills that coroutine with StopAllCoroutines first.
/// </summary>
public sealed class ImportAndCaptureCleanupContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, $"missing source: {path}");
		return File.ReadAllText(path);
	}

	static string ImportHelper() => Read("Assets", "_gm", "Features", "3D Models",
		"ModelsHandler3D_ImportHelper.cs");

	static string ScreenshotMgr() => Read("Assets", "_gm", "Features", "TextureTools", "Screenshot",
		"Screenshot_MGR.cs");

	[Test]
	public void ImportStartFailureClearsTheImportingFlag() {
		string src = ImportHelper();
		int i = src.IndexOf("public void ImportModel_via_Filepath(", StringComparison.Ordinal);
		int end = src.IndexOf("IEnumerator ImportRoutine(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		Assert.That(end, Is.GreaterThan(i));
		string body = src.Substring(i, end - i);

		int flag = body.IndexOf("_isImportingModel = true;", StringComparison.Ordinal);
		int start = body.IndexOf("StartCoroutine(ImportRoutine(", StringComparison.Ordinal);
		Assert.That(flag, Is.GreaterThan(0));
		Assert.That(start, Is.GreaterThan(flag), "the flag is taken before the coroutine starts");

		// Everything between taking the flag and starting the coroutine must be recoverable.
		string risky = body.Substring(flag);
		Assert.That(risky, Does.Contain("_Act_onStartedImporting?.Invoke();"));
		Assert.That(risky, Does.Contain("catch (Exception e)"),
			"a throwing start listener must not strand _isImportingModel");
		Assert.That(risky, Does.Contain("OnError(\"Could not start the import: \" + e.Message);"),
			"the failure must route through OnError, which clears the flag and reports completion");

		int guard = risky.IndexOf("try {", StringComparison.Ordinal);
		Assert.That(guard, Is.GreaterThanOrEqualTo(0).And.LessThan(
			risky.IndexOf("StartCoroutine(ImportRoutine(", StringComparison.Ordinal)),
			"the guard must wrap the announce AND the coroutine launch");
	}

	[Test]
	public void OnErrorIsTheSingleFlagReleasePoint() {
		string src = ImportHelper();
		int i = src.IndexOf("void OnError(string errorMsg)", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void OnSuccess_AcceptModel(", i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i), "anchor on the real method, not a fixed-width window");
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("_isImportingModel = false;"));
		Assert.That(body, Does.Contain("_Act_onImportComplete?.Invoke(false, null);"),
			"callers waiting on import completion must be released too");
	}

	[Test]
	public void CancelDestroysInFlightCaptureTextures() {
		string src = ScreenshotMgr();

		Assert.That(src, Does.Contain("_inFlightCaptureRTs"),
			"the capture RTs must be reachable from outside the coroutine");

		int cancel = src.IndexOf("void CancelPendingScriptCapture()", StringComparison.Ordinal);
		Assert.That(cancel, Is.GreaterThan(0));
		string cancelBody = src.Substring(cancel, Math.Min(500, src.Length - cancel));
		int stop = cancelBody.IndexOf("StopAllCoroutines();", StringComparison.Ordinal);
		int destroy = cancelBody.IndexOf("DestroyInFlightCaptureRTs();", StringComparison.Ordinal);
		Assert.That(stop, Is.GreaterThan(0));
		Assert.That(destroy, Is.GreaterThan(stop),
			"the RTs must be destroyed after the coroutine that owned them is killed");
	}

	[Test]
	public void CaptureTracksBothTexturesAndReleasesThemOnSuccess() {
		string src = ScreenshotMgr();
		int i = src.IndexOf("IEnumerator MakeScreenshot_crtn(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void ReleaseCaptureRT(", StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i));
		string body = src.Substring(i, end - i);

		Assert.That(body, Does.Contain("_inFlightCaptureRTs.Add(screenRT);"));
		Assert.That(body, Does.Contain("_inFlightCaptureRTs.Add(portionRT);"));
		Assert.That(body, Does.Contain("ReleaseCaptureRT(screenRT);"));
		Assert.That(body, Does.Contain("ReleaseCaptureRT(portionRT);"));
		Assert.That(body, Does.Not.Contain("DestroyImmediate(screenRT);"),
			"the success path must also untrack, otherwise the list keeps destroyed entries");
	}

	[Test]
	public void TeardownAlsoDestroysInFlightCaptureTextures() {
		string src = ScreenshotMgr();
		int i = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, Math.Min(300, src.Length - i));
		Assert.That(body, Does.Contain("DestroyInFlightCaptureRTs();"));
	}
}
