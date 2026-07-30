using System.IO;
using NUnit.Framework;

/// <summary>
/// Deferred import RPC must not inherit success from a previous import.
/// </summary>
public sealed class SpzGoImportLastSuccessResetTests {

	[Test]
	public void ImportModel_ClearsLastSuccessAtStart() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int start = src.IndexOf("public void ImportModel_via_Filepath");
		Assert.That(start, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("IEnumerator ImportRoutine", start);
		Assert.That(end, Is.GreaterThan(start));
		string body = src.Substring(start, end - start);
		Assert.That(body, Does.Contain("_lastImportSucceeded = false"),
			"Must clear prior import success before starting a new load.");
	}
}
