using System;
using System.IO;
using NUnit.Framework;

public sealed class HuntWave48to50ContractTests {

	static string Read(params string[] parts) =>
		File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));

	[Test]
	public void SetSkyboxColor_RequiresIsTopBool() {
		string src = Read("Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		int i = src.IndexOf("case \"spz.cmd.set_skybox_color\":", StringComparison.Ordinal);
		string body = src.Substring(i, Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("is_top bool required"));
		Assert.That(body, Does.Not.Contain("?? true"));
	}

	[Test]
	public void WorkflowOptionsRibbon_UnsubscribesStaticHandlersOnDestroy() {
		string src = Read("Assets", "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		int d = src.IndexOf("void OnDestroy()", StringComparison.Ordinal);
		string body = src.Substring(d, Math.Min(900, src.Length - d));
		Assert.That(body, Does.Contain("WorkflowRibbon_UI._Act_OnModeChanged -= OnModeChanged"));
		Assert.That(body, Does.Contain("Act_onWillSendOptions_AmmendPlz -= OnWillSendOptions_AmmendPlz"));
		Assert.That(body, Does.Contain("_Act_img2img_requested -= On_img2img_requested"));
	}

	[Test]
	public void AgentScreenshot_HasWatchdogClearingInFlight() {
		string src = Read("Assets", "_gm", "Features", "AgentBridge", "SPZ_Agent_Tools.cs");
		Assert.That(src, Does.Contain("ScreenshotWatchdog_crtn"));
		Assert.That(src, Does.Contain("Screenshot timed out"));
		Assert.That(src, Does.Contain("_screenshotFlightGen"));
	}
}
