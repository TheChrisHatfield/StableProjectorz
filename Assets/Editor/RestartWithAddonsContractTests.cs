using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// "Restart with addons" must outlive Application.Quit. Player.log showed the bat launching then
/// the app closing with no new instance — children died with Unity's process Job, and/or the bat
/// started the exe while the old PID was still alive.
/// </summary>
public class RestartWithAddonsContractTests {

	[Test]
	public void RestartWithAddons_WaitsForSelfPidAndBreaksAwayFromJob() {
		string launchPath = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "Launch_Addons_Bat_File.cs");
		string procPath = Path.Combine(Application.dataPath, "_gm", "_Core", "IO", "IL2cppStartProcess", "StartExternalProcess.cs");
		string batPath = Path.Combine(Directory.GetCurrentDirectory(), "Run_with_Addons.bat");
		Assert.That(File.Exists(launchPath), Is.True, "Launch_Addons_Bat_File.cs missing: " + launchPath);
		Assert.That(File.Exists(procPath), Is.True, "StartExternalProcess.cs missing: " + procPath);
		Assert.That(File.Exists(batPath), Is.True, "Run_with_Addons.bat missing: " + batPath);

		string launch = File.ReadAllText(launchPath);
		string proc = File.ReadAllText(procPath);
		string bat = File.ReadAllText(batPath);

		Assert.That(launch, Does.Contain("waitForPid: selfPid"),
			"Restart must pass current PID so the bat waits for this process to exit before relaunch.");
		Assert.That(launch, Does.Contain("breakAwayFromJob: true"),
			"Restart helper must break away from Unity's Job or quit kills the relaunch.");
		Assert.That(launch, Does.Contain("GetCurrentPid()"),
			"Restart must read the live process id to hand to the bat.");
		Assert.That(proc, Does.Contain("CREATE_BREAKAWAY_FROM_JOB"),
			"StartExternalProcess must support CREATE_BREAKAWAY_FROM_JOB for restart.");
		Assert.That(proc, Does.Contain("breakAwayFromJob"),
			"Run_Bat_or_Shortcut_or_Command must accept breakAwayFromJob.");
		Assert.That(bat, Does.Contain("WAIT_PID"),
			"Run_with_Addons.bat must accept a PID and wait for it to exit.");
		Assert.That(bat, Does.Contain("SPZ_ADDONS_NONINTERACTIVE"),
			"Hidden restart must not block forever on pause.");
		Assert.That(bat, Does.Contain("start \"\" \"%EXE%\""),
			"Bat must actually launch the player exe.");
		Assert.That(launch, Does.Contain("Restart already in progress"),
			"Duplicate Restart click must ShowRestartStatus so the disabled button is re-enabled.");
	}

	/// <summary>
	/// `echo Ensuring addon dependencies (FastAPI, uvicorn)...` inside a parenthesized if-block
	/// aborted the whole bat with "... was unexpected at this time." — the exe was never started,
	/// so in-app "Restart with addons" looked like a plain close. Unescaped ( ) in echo is the trap.
	/// </summary>
	[Test]
	public void RestartBat_HasNoUnescapedParensInEchoLines() {
		string batPath = Path.Combine(Directory.GetCurrentDirectory(), "Run_with_Addons.bat");
		Assert.That(File.Exists(batPath), Is.True, "Run_with_Addons.bat missing: " + batPath);
		string[] lines = File.ReadAllLines(batPath);
		for (int i = 0; i < lines.Length; i++) {
			string trimmed = lines[i].Trim();
			if (!trimmed.StartsWith("echo ", System.StringComparison.OrdinalIgnoreCase)) continue;
			for (int c = 0; c < trimmed.Length; c++) {
				char ch = trimmed[c];
				if (ch != '(' && ch != ')') continue;
				bool escaped = c > 0 && trimmed[c - 1] == '^';
				Assert.That(escaped, Is.True,
					$"Run_with_Addons.bat line {i + 1} has an unescaped '{ch}' in an echo — cmd aborts the " +
					$"enclosing block and the exe never launches. Use ^( / ^). Line: {trimmed}");
			}
		}
	}
}
