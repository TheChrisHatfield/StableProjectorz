using System.IO;
using NUnit.Framework;

/// <summary>
/// DoSaveProject must not set _isSaving before SaveProject or the helper self-deadlocks.
/// </summary>
public sealed class SpzGoDoSaveProjectBusyGuardTests {

	[Test]
	public void DoSaveProject_DoesNotSetIsSavingBeforeSaveProject() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("public void DoSaveProject()");
		Assert.That(method, Is.GreaterThan(0));
		int nextMethod = src.IndexOf("public void DoLoadProject()", method + 1);
		Assert.That(nextMethod, Is.GreaterThan(method));
		string body = src.Substring(method, nextMethod - method);
		int setSaving = body.IndexOf("_isSaving = true");
		int callSave = body.IndexOf("SaveProject(");
		Assert.That(callSave, Is.GreaterThan(0));
		Assert.That(setSaving, Is.GreaterThan(callSave),
			"_isSaving must be set only after SaveProject supplies a real path, not before the call.");
		Assert.That(body, Does.Contain("if( _isSaving )"),
			"DoSaveProject must refuse when an export already owns _isSaving.");
	}
}
