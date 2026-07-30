using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Soft/Inpaint RolesUnder must not SolidSquare BrushRibbon hardness stamps / color swatches.
/// </summary>
public sealed class SoftRolesUnderBrushStampThemeTests {

	[Test]
	public void SoftWorkflowOptions_RolesUnderExcludesBrushStampHosts() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Hardness>(true)"));
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Colors>(true)"));
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Size>(true)"));
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_BucketFill>(true)"));
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Direction>(true)"));
		Assert.That(src, Does.Contain("GetComponentInParent<BrushRibbon_UI_Opacity>(true)"));
	}
}
