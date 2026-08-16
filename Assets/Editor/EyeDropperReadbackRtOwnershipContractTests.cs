using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Eyedropper sampling holds its screen RT until the async GPU readback resolves.
/// AsyncGPUReadback completes some frames after the request, so returning the RT to the temporary
/// pool right away lets the next PickColor iteration check out the very same descriptor
/// (Screen.width x Screen.height) and overwrite the pixel being sampled — a wrong or black color.
/// This cannot be exercised headlessly (no GPU readback in batchmode), so the ownership handoff is
/// pinned here against the real method blocks.
/// </summary>
public sealed class EyeDropperReadbackRtOwnershipContractTests {

	static string ReadEyeDropper() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI_EyeDropperTool.cs");
		return File.ReadAllText(path);
	}

	static string Block(string src, string startMarker, string endMarker) {
		int i = src.IndexOf(startMarker, StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0), $"missing block start: {startMarker}");
		int end = src.IndexOf(endMarker, i, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(i), $"missing block end: {endMarker}");
		return src.Substring(i, end - i);
	}

	[Test]
	public void PickColorCoroutine_DoesNotReleaseTheRtItHandedToTheReadback() {
		string body = Block(ReadEyeDropper(), "IEnumerator PickColor_crtn(", "void OnCompleteReadback(");

		int start = body.IndexOf("StartAsyncGPUReadback(", StringComparison.Ordinal);
		int release = body.IndexOf("ReleaseTemporary(", StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThan(0), "the sampling branch must start a readback");
		Assert.That(release, Is.GreaterThan(start), "the release must follow the readback branch");

		Assert.That(body, Does.Contain("readbackOwnsRT = true;"),
			"starting a readback must transfer ownership of the temp RT");
		Assert.That(body, Does.Contain("if(!readbackOwnsRT){ RenderTexture.ReleaseTemporary(tempScreenRT); }"),
			"the coroutine may only release the RT when no readback is pending on it");

		// The regression shape: a release sitting at the end of the branch with nothing guarding it.
		int guard = body.IndexOf("if(!readbackOwnsRT)", StringComparison.Ordinal);
		Assert.That(guard, Is.GreaterThan(0).And.LessThan(release),
			"the guard must precede the release, not follow it");
	}

	[Test]
	public void ReadbackCallback_ReleasesTheRtInAFinally() {
		string body = Block(ReadEyeDropper(), "public void StartAsyncGPUReadback(", "void OnCompleteReadback(");

		Assert.That(body, Does.Contain("AsyncGPUReadback.Request("));
		Assert.That(body, Does.Contain("finally"),
			"a throwing readback handler must still return the RT to the pool");

		int request = body.IndexOf("AsyncGPUReadback.Request(", StringComparison.Ordinal);
		int release = body.IndexOf("ReleaseTemporary(", StringComparison.Ordinal);
		Assert.That(release, Is.GreaterThan(request),
			"the owner of the RT is the readback callback, so the release lives inside the request");
	}
}
