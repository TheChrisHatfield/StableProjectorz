using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ThemePassColorsServClearNonFaceTests {
	[Test]
	public void ColorsSlideout_UsesThemeCheckboxToggle() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "WorkflowToolsRibbon SD", "WorkflowRibbon_Colors_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeCheckboxToggle"));
	}

	[Test]
	public void SceneResolution_PlusMinusClearNonFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "SceneResolution_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_sub_texResolutionQuality)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_add_texResolutionQuality)"));
	}

	[Test]
	public void RestartWebui_LaunchGetsLineIcon() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Webui", "RestartTheWebui.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("StudioLineIcon.Bullseye"));
	}

	[Test]
	public void ConnectionOpen_GetsGlobeLineIcon() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Connection", "ConnectionPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ApplyControlLineIconLeading(_openPanel_button.transform, StudioLineIcon.Globe"));
	}

	[Test]
	public void ContextMenuChrome_SkipsGenerateButtonsUi() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeOps.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("GenerateButtons_UI"));
	}

	[Test]
	public void AddonManager_SnapshotsHeaderChildAlignment() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreHeaderChildAlignment"));
		Assert.That(src, Does.Contain("_authoredHeaderChildAlignment"));
	}

	[Test]
	public void ValueAssist_NotDoubleSolidSquaredByCollect() {
		string collect = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		string assist = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_ValueAssistPanel_UI.cs");
		Assert.That(File.ReadAllText(collect), Does.Contain("PaintTab_ValueAssistPanel_UI"));
		string a = File.ReadAllText(assist);
		Assert.That(a, Does.Contain("ApplyContextMenuChrome"));
		Assert.That(a, Does.Not.Contain("ApplyBoundChromeSelectable(btn"));
	}

	[Test]
	public void ColorPalettePanel_NotDoubleSolidSquaredByCollect() {
		string collect = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(File.ReadAllText(collect), Does.Contain("ColorPalette_Panel_UI"));
		Assert.That(File.ReadAllText(collect), Does.Contain("GetComponentInParent<ColorPalette_Panel_UI>"));
	}
}
