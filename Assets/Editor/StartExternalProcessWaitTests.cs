using System.IO;
using NUnit.Framework;

/// <summary>
/// IL2CPP-safe process helper must not treat OpenProcess failure as "already exited".
/// </summary>
public sealed class StartExternalProcessWaitTests {

	[Test]
	public void WaitForProcessExit_DoesNotAssumeDeadWhenOpenFails() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "IO", "IL2cppStartProcess", "StartExternalProcess.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PROCESS_WAIT_ACCESS"),
			"Wait must use SYNCHRONIZE|QUERY_LIMITED, not PROCESS_ALL_ACCESS.");
		Assert.That(src, Does.Not.Contain("return true; // Process doesn't exist"),
			"OpenProcess failure must not report immediate exit (empty install logs).");
		Assert.That(src, Does.Contain("IsProcessRunning(processId)"),
			"When OpenProcess fails, poll IsProcessRunning instead of assuming exit.");
		Assert.That(src, Does.Contain("remaining"),
			"After OpenProcess poll, WaitForSingleObject must use remaining timeout, not a fresh full budget.");
	}
}
