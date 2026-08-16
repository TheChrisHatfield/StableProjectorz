using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// A failed import must not cost the user the model already in the scene.
/// The start callback is destructive (ModelsHandler_3D.OnStartedImporting → Remove_CurrentModel),
/// so anything that can fail and abort the import has to run before it.
/// </summary>
public sealed class ImportKeepsModelUntilFileIsReadableTests {

	static string ImportHelperSource() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void FileIsReadBeforeTheDestructiveStartCallback() {
		string src = ImportHelperSource();
		int method = src.IndexOf("public void ImportModel_via_Filepath( string filepath )", StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int end = src.IndexOf("void OnError(", method, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(method));
		string body = src.Substring(method, end - method);

		int read = body.IndexOf("File.ReadAllBytes(filepath)", StringComparison.Ordinal);
		int started = body.IndexOf("_Act_onStartedImporting?.Invoke()", StringComparison.Ordinal);
		Assert.That(read, Is.GreaterThan(0), "import must cache the model bytes");
		Assert.That(started, Is.GreaterThan(0), "import must announce that it started");

		// SPZ imports the exchange FBX the moment Blender stamps it ready, so a locked or half-flushed
		// file is the common case, not a rare one. Reading after the removal wipes the scene and
		// imports nothing.
		Assert.That(read, Is.LessThan(started),
			"the file must be read before the start callback removes the current model");
	}

	[Test]
	public void ReadFailureStillReportsCompletion() {
		string src = ImportHelperSource();
		int method = src.IndexOf("public void ImportModel_via_Filepath( string filepath )", StringComparison.Ordinal);
		int end = src.IndexOf("void OnError(", method, StringComparison.Ordinal);
		string body = src.Substring(method, end - method);
		Assert.That(body, Does.Contain("OnError(\"Could not read file: \""),
			"an unreadable file must go through OnError, which clears _isImportingModel and notifies waiters");

		// OnError must remain safe to call when the start callback never ran.
		int onError = src.IndexOf("void OnError(string errorMsg)", StringComparison.Ordinal);
		int onErrorEnd = src.IndexOf("void OnSuccess_AcceptModel(", onError, StringComparison.Ordinal);
		string errBody = src.Substring(onError, onErrorEnd - onError);
		Assert.That(errBody, Does.Contain("_isImportingModel = false"),
			"a failed import must release the import lock or every later import is refused");
		Assert.That(errBody, Does.Contain("_lastImportSucceeded = false"));
		Assert.That(errBody, Does.Contain("_Act_onImportComplete?.Invoke(false, null)"),
			"deferred RPC callers wait on this; skipping it hangs them");
	}
}
