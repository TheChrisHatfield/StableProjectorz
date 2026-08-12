using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ImportCnFullSrnLeaveTests {
	[Test]
	public void ImportToLayer_LeaveUsesRestoreBoundChromeUnder_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Icons", "IconUI", "IconUI_Art2D_ContextMenu.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ThemeImportToLayerButton", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(_button_importToLayer.transform)"));
	}

	[Test]
	public void ControlNetTitle_CapturesAuthoredDesignPt_NotEnsure14_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ThemeControlNetUnitTitle", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1200, src.Length - idx));
		Assert.That(body, Does.Contain("ResolveOrCaptureDesignFontPt"));
		Assert.That(body, Does.Not.Contain("EnsureDesignFontPt(_mainHeader, 14f)"));
	}

	[Test]
	public void FullSrnLabel_LeaveDoesNotReforceBoldOutline_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "RibbonViewportFullViewOnScreen_Toggle_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFullSrnLabelStyle", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(1600, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotToolFaceLayout(textRt)"));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(tmp.transform)"));
		Assert.That(body, Does.Not.Contain("EnsureDesignFontPt(tmp, DockLabelBasePt)"));
	}
}
