using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class SoftBrushRibbonRolesExcludeTests {
	[Test]
	public void SoftAndGen3D_ExcludeBrushRibbonViaSharedHelper_Source() {
		string brush = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		string soft = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		string gen3d = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Gen3D_WorkflowOptionsRibbon_UI.cs");
		Assert.That(File.ReadAllText(brush), Does.Contain("IsBoundChromeOwnedByBrushRibbon"));
		Assert.That(File.ReadAllText(brush), Does.Contain("BrushRibbon_UI_EyeDropperTool"));
		Assert.That(File.ReadAllText(brush), Does.Contain("BrushRibbon_UI_AlphaPicker"));
		Assert.That(File.ReadAllText(soft), Does.Contain("BrushRibbon_UI.IsBoundChromeOwnedByBrushRibbon(c)"));
		Assert.That(File.ReadAllText(gen3d), Does.Contain("BrushRibbon_UI.IsBoundChromeOwnedByBrushRibbon(c)"));
	}
}
