using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PaintSettingsRibbonLeaveHoldoverTests {
	[Test]
	public void SettingsToggle_LeaveRestoresBoundChrome_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "Settings_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyThemeToggleColors", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(500, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(tgl.transform)"));
	}

	[Test]
	public void LayersPanel_LeaveRestoresPanelRoot_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public void ApplyThemeTokens", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(700, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(transform)"));
	}

	[Test]
	public void Collect_SkipsValueAssistTmpAndToggles_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("tmp.GetComponentInParent<PaintTab_ValueAssistPanel_UI>(true) != null)"));
		Assert.That(src, Does.Contain("toggle.GetComponentInParent<PaintTab_ValueAssistPanel_UI>(true) != null)"));
	}

	[Test]
	public void BuiltinAddonStrip_DoesNotEnsureHitFaceOnLeave_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Layouts", "RightPanel", "CommandRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("if (!recolorChrome && builtinAddonIconStrip)", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("FindStripTabFaceImage(cell)"));
		Assert.That(body, Does.Not.Contain("EnsureStripTabHitFace(cell)"));
	}
}
