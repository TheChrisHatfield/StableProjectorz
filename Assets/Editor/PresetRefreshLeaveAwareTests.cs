using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PresetRefreshLeaveAwareTests {
	[Test]
	public void PromptPresetRefresh_NoLongerEarlyReturnsOnLeave_Source() {
		string sd = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Input Panel", "SD_InputPanel_UI.cs");
		string gen = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Generation3D_Prompt_UI.cs");
		string sdSrc = File.ReadAllText(sd);
		int a = sdSrc.IndexOf("public void RefreshPromptPresetChrome", System.StringComparison.Ordinal);
		string aBody = sdSrc.Substring(a, System.Math.Min(400, sdSrc.Length - a));
		Assert.That(aBody, Does.Not.Contain("if (!SpzUiThemeOps.ShouldRecolorBoundChrome) return;"));

		int b = sdSrc.IndexOf("public void RefreshResolutionPresetChrome", System.StringComparison.Ordinal);
		string bBody = sdSrc.Substring(b, System.Math.Min(400, sdSrc.Length - b));
		Assert.That(bBody, Does.Not.Contain("if (!SpzUiThemeOps.ShouldRecolorBoundChrome) return;"));

		string genSrc = File.ReadAllText(gen);
		int c = genSrc.IndexOf("public void RefreshPresetChrome", System.StringComparison.Ordinal);
		string cBody = genSrc.Substring(c, System.Math.Min(500, genSrc.Length - c));
		Assert.That(cBody, Does.Not.Contain("!SpzUiThemeOps.ShouldRecolorBoundChrome ||"));
	}
}
