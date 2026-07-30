using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class Gen3dSoftBrushDirectionSiloThemeTests {

	[Test]
	public void Gen3dWorkflowOptions_RolesUnderExcludesBrushDirection() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Gen3D_WorkflowOptionsRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Direction>(true)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_direction.transform)"));
	}
}
