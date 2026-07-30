using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ConfirmBrushAddonSaveBlendThemeTests {
	[Test]
	public void ConfirmPopup_EnsuresHitFaceBeforeClear() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)",
			"Widgets and Gadgets", "UI_ConfirmPopup_YesNo", "ConfirmPopup_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemePopupButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(800, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
	}

	[Test]
	public void BrushRibbon_ThemeToolButtonEnsuresHitFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemeToolButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(900, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
	}

	[Test]
	public void AddonClose_EnsuresHitFaceBeforeClear() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_closePanel_button)"));
	}

	[Test]
	public void SceneResolution_EnsuresSaveAndPlusMinusFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Settings", "SceneResolution_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(saveBtn)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_sub_texResolutionQuality)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_add_texResolutionQuality)"));
	}

	[Test]
	public void MultiviewBlend_SkipsAuthoredIconSolidSquare() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(_BlendCams_button.targetGraphic)"));
	}
}
