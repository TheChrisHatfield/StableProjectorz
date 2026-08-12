using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class RefreshChromeLeaveUnwindTests {
	[Test]
	public void SelectionRefreshHelpers_UnwindOnLeave_Source() {
		string cn = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs");
		string sd = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		string gen3d = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Gen3D_WorkflowOptionsRibbon_UI.cs");
		string mv = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		string brush = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");

		Assert.That(File.ReadAllText(cn), Does.Contain("RestoreBoundChromeUnder(transform)"));
		Assert.That(File.ReadAllText(sd), Does.Contain("RestoreBoundChromeUnder(_softInpaint.transform)"));
		Assert.That(File.ReadAllText(gen3d), Does.Contain("RestoreBoundChromeUnder(_showAlphaOnly_toggle.transform)"));
		Assert.That(File.ReadAllText(mv), Does.Contain("RestoreBoundChromeUnder(_showGrid_toggle.transform)"));
		Assert.That(File.ReadAllText(brush), Does.Contain("RestoreBoundChromeUnder(_pressureTabletMode.transform)"));
	}
}
