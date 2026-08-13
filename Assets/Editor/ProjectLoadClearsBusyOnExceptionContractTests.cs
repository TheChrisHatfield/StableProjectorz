using System.IO;
using NUnit.Framework;

/// <summary>
/// Project load must always clear Save_MGR._isLoading / invoke onResult even when apply throws.
/// </summary>
public sealed class ProjectLoadClearsBusyOnExceptionContractTests {

	[Test]
	public void LoadProject_Source_TryCatchInvokesOnResultOnException() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void LoadProject(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("public void", i + 10, System.StringComparison.Ordinal);
		if (end < 0) end = System.Math.Min(src.Length, i + 4500);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("catch (System.Exception"));
		Assert.That(body, Does.Contain("Error loading the project:"));
		Assert.That(body, Does.Contain("LastProjectLoadSucceeded = false"));
		Assert.That(body, Does.Contain("Performance_MGR.instance?.Load"),
			"null managers must not NRE mid-load and stick the busy gate");
	}

	[Test]
	public void DoLoadProject_Source_ClearsIsLoadingInFinally() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("_isLoading = true", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("finally"));
		Assert.That(body, Does.Contain("_isLoading = false"));
	}
}
