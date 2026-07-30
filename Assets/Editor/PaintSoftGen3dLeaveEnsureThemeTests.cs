using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PaintSectionsLeaveThemeTests {

	[Test]
	public void PaintCollect_LeaveRestoresKritaSectionRoots() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreOwnedSection(_layout.ToolOptionsSection)"));
		Assert.That(src, Does.Contain("RestoreOwnedSection(_layout.LayersSection)"));
		Assert.That(src, Does.Contain("RestoreOwnedSection(_layout.ToolchestRow)"));
	}
}

public sealed class SoftBrushHostsLeaveThemeTests {

	[Test]
	public void SoftWorkflowOptions_LeaveRestoresBrushStampHosts() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "SD_WorkflowOptionsRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_brushHardness.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_brushColor.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_brushSize_slider.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_bucketFill.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_direction.transform)"));
	}
}

public sealed class Gen3dRembgEnsureThemeTests {

	[Test]
	public void Gen3dWorkflowOptions_RembgEnsuresHitFaceBeforeClear() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Gen3D_WorkflowOptionsRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_rembg_button)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_rembg_button)"));
	}
}
