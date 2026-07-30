using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class Art3dListShellOwnerThemeTests {
	[Test]
	public void Art3dList_DoesNotDualThemeRootPanelShell() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Art3D_IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CommandRibbon_UI — skip dual root tint"));
		Assert.That(src, Does.Not.Contain("ApplyBoundChromeGraphic(rootImg, t.panelBg)"));
	}
}

public sealed class SubMeshRmvGlyphThemeTests {
	[Test]
	public void SubMeshIcon_RmvSkipsAuthoredIconSolidSquare() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Models", "UI", "SD_subMesh_IconUI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(_rmvButton.targetGraphic)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_rmvButton)"));
	}
}

public sealed class Gen3dVideoPreviewDecisionThemeTests {
	[Test]
	public void VideoPreview_DecisionButtonsEnsureAndGlyphSafe() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate",
			"Gen3D_InputPanelBuilder_UI", "Gen3D_VideoPreview_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("static void ThemeDecisionButton");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(1200, src.Length - ix));
		Assert.That(body, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(body, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
	}
}

public sealed class ColorsBakeGlyphThemeTests {
	[Test]
	public void ColorsBake_EnsuresAndSkipsAuthoredIconFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "WorkflowRibbon_Colors_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_bakeColors_button)"));
		Assert.That(src, Does.Contain("IsAuthoredIconFace(_bakeColors_button.targetGraphic)"));
	}
}

public sealed class PayMoneyEnsureThemeTests {
	[Test]
	public void PayMoney_EnsuresHitFaceBeforeCompactClear() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Viewport", "Main Viewport", "PayMoney_button.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(_button)"));
		Assert.That(src.IndexOf("EnsureSelectableHitFace"), Is.LessThan(src.IndexOf("ClearNonFaceRaycastsForTheme")));
	}
}

public sealed class ExportSaveGlyphThemeTests {
	[Test]
	public void ExportSave_ThemeMenuButtonSkipsAuthoredIconFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Save Load Import Export", "ExportSave_UI_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
	}
}
