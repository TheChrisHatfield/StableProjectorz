using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// A failed import must not cost the user the model already in the scene.
/// Bytes are read first; Assimp runs before the start callback; the live model is only parked
/// for Init and destroyed after Init succeeds (ModelsHandler_3D park/restore).
/// </summary>
public sealed class ImportKeepsModelUntilFileIsReadableTests {

	static string ImportHelperSource() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	static string ModelsHandlerSource() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler_3D.cs");
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	/// Anchor on the signature prefix: pinning the full parameter list makes this test evaporate the
	/// next time a parameter is added, which is exactly when it still needs to hold.
	const string ImportEntry = "public void ImportModel_via_Filepath( string filepath";

	[Test]
	public void FileIsReadBeforeTheDestructiveStartCallback() {
		string src = ImportHelperSource();
		int method = src.IndexOf(ImportEntry, StringComparison.Ordinal);
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
	public void AssimpLoadHappensBeforeStartCallbackAndPark() {
		string src = ImportHelperSource();
		int routine = src.IndexOf("IEnumerator ImportRoutine(", StringComparison.Ordinal);
		Assert.That(routine, Is.GreaterThan(0));
		int end = src.IndexOf("void OnError(", routine, StringComparison.Ordinal);
		string body = src.Substring(routine, end - routine);

		int load = body.IndexOf("loader.Load(filepath", StringComparison.Ordinal);
		int started = body.IndexOf("_Act_onStartedImporting?.Invoke()", StringComparison.Ordinal);
		Assert.That(load, Is.GreaterThan(0));
		Assert.That(started, Is.GreaterThan(load),
			"Assimp must succeed before WillLoadModel / park — otherwise a bad FBX empties the scene");
		Assert.That(body, Does.Contain("OnError"),
			"Assimp failure must report without parking");
	}

	[Test]
	public void InitFailureRestoresParkedModelBeforeReporting() {
		string helper = ImportHelperSource();
		int accept = helper.IndexOf("void OnSuccess_AcceptModel(", StringComparison.Ordinal);
		Assert.That(accept, Is.GreaterThan(0));
		int end = helper.IndexOf("void OnUDIMsProgress01(", accept, StringComparison.Ordinal);
		string body = helper.Substring(accept, end - accept);

		Assert.That(body, Does.Contain("ParkCurrentModelForImportReplace"));
		Assert.That(body, Does.Contain("RestoreParkedModelAfterFailedImport"));
		Assert.That(body, Does.Contain("CommitParkedModelDiscard"));

		int park = body.IndexOf("ParkCurrentModelForImportReplace", StringComparison.Ordinal);
		int init = body.IndexOf("o3d.Init(loadedRoot)", StringComparison.Ordinal);
		int commit = body.IndexOf("CommitParkedModelDiscard", StringComparison.Ordinal);
		Assert.That(park, Is.LessThan(init));
		Assert.That(init, Is.LessThan(commit),
			"only destroy the parked prior model after Init succeeds");

		string mh = ModelsHandlerSource();
		Assert.That(mh, Does.Contain("ParkCurrentModelForImportReplace"));
		Assert.That(mh, Does.Contain("RestoreParkedModelAfterFailedImport"));
		int started = mh.IndexOf("void OnStartedImporting()", StringComparison.Ordinal);
		int startedEnd = mh.IndexOf("void OnImportModel_Done(", started, StringComparison.Ordinal);
		string startBody = mh.Substring(started, startedEnd - started);
		Assert.That(startBody, Does.Not.Contain("Remove_CurrentModel()"),
			"start must not wipe the scene before Assimp/Init");
	}

	[Test]
	public void ReadFailureStillReportsCompletion() {
		string src = ImportHelperSource();
		int method = src.IndexOf(ImportEntry, StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int end = src.IndexOf("void OnError(", method, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(method));
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
