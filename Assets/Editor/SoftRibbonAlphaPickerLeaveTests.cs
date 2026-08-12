using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class SoftRibbonAlphaPickerLeaveTests {
	[Test]
	public void SoftLeave_RestoresAlphaPicker_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("FindFirstObjectByType<BrushRibbon_UI_AlphaPicker>"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(alphaPicker.transform)"));
	}
}
