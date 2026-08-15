using System.IO;
using NUnit.Framework;

/// <summary>
/// DoLoadProject must refuse while saving/loading/generating — same gates as FastPath_API.LoadProject.
/// </summary>
public sealed class DoLoadProjectBusyGuardTests {

	[Test]
	public void DoLoadProject_RefusesWhileBusyLikeFastPath() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("public void DoLoadProject(string filepath)", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("IEnumerator ResetCtrlKey_AfterLoadSave()", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("_isSaving || _isLoading"),
			"Ctrl+L / UI load must refuse while export or another load owns the flags.");
		Assert.That(body.IndexOf("if( _isSaving || _isLoading )", System.StringComparison.Ordinal),
			Is.LessThan(body.IndexOf("_isLoading = true", System.StringComparison.Ordinal)),
			"Must not claim _isLoading before the busy check.");
		Assert.That(body, Does.Contain("_generating"),
			"Must refuse load while SD is generating (parity with FastPath).");
	}
}
