using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class LauncherAndModeToggleLeaveTests {
	[Test]
	public void ThemeFlatLauncher_And_OpenLauncher_LeaveRestore_Source() {
		string settings = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "Settings_UI.cs");
		string addon = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string workflow = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "WorkflowRibbon_UI.cs");
		Assert.That(File.Exists(settings), Is.True);
		Assert.That(File.Exists(addon), Is.True);
		Assert.That(File.Exists(workflow), Is.True);

		string settingsSrc = File.ReadAllText(settings);
		int flat = settingsSrc.IndexOf("static void ThemeFlatLauncherButton", System.StringComparison.Ordinal);
		string flatBody = settingsSrc.Substring(flat, System.Math.Min(700, settingsSrc.Length - flat));
		Assert.That(flatBody, Does.Contain("RestoreBoundChromeUnder(btn.transform)"));
		Assert.That(flatBody, Does.Contain("HideMonolithUnder(btn.transform)"));

		string addonSrc = File.ReadAllText(addon);
		int open = addonSrc.IndexOf("void ThemeOpenLauncherButton", System.StringComparison.Ordinal);
		string openBody = addonSrc.Substring(open, System.Math.Min(700, addonSrc.Length - open));
		Assert.That(openBody, Does.Contain("RestoreBoundChromeUnder(_openPanel_button.transform)"));

		string wfSrc = File.ReadAllText(workflow);
		int mode = wfSrc.IndexOf("static void ThemeModeToggle", System.StringComparison.Ordinal);
		string modeBody = wfSrc.Substring(mode, System.Math.Min(900, wfSrc.Length - mode));
		Assert.That(modeBody, Does.Contain("RestoreWorkflowModeAuthored(modeUi)"));
		Assert.That(modeBody, Does.Contain("SnapshotAuthoredColorBlock(toggle)"));
	}
}
