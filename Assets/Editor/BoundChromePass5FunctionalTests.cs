using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 5 BoundChrome: Mask/Filled disc skips, rembg dial leave, CommandRibbon API litmus.
/// </summary>
public sealed class BoundChromePass5FunctionalTests {

	[TearDown]
	public void TearDown() {
		SpzUiThemeOps.ResetTheme();
	}

	[Test]
	public void FlattenToolFaceImage_SkipsFilledRadial() {
		Assert.That(SpzUiThemeOps.TryApplyTheme(
			"p1-experiment",
			new JObject { ["control_bg"] = "#292A2EFF" },
			"replace",
			out string error), Is.True, error);

		var go = new GameObject("FilledFace", typeof(RectTransform), typeof(Image));
		try {
			var img = go.GetComponent<Image>();
			var authored = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
			img.sprite = authored;
			img.type = Image.Type.Filled;
			img.fillMethod = Image.FillMethod.Radial360;
			img.pixelsPerUnitMultiplier = 7f;
			SpzUiThemeOps.FlattenToolFaceImage(img);
			Assert.That(img.type, Is.EqualTo(Image.Type.Filled));
			Assert.That(img.pixelsPerUnitMultiplier, Is.EqualTo(7f).Within(0.01f));
			Assert.That(ReferenceEquals(img.sprite, authored), Is.True);
		}
		finally {
			Object.DestroyImmediate(go);
		}
	}

	[Test]
	public void DimensionMode_SourceSkipsMaskAndFilledInApplyFlatDisc() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/Viewport (MainView)/DimensionMode_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFlatDisc(Image", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("IsUiMaskGraphic"));
		Assert.That(body, Does.Contain("Image.Type.Filled"));
	}

	[Test]
	public void Gen3DWorkflow_LeaveRestoresRembgDials() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int apply = src.IndexOf("void ApplyThemeTokens()", System.StringComparison.Ordinal);
		int leave = src.IndexOf("if (!SpzUiThemeOps.ShouldRecolorBoundChrome)", apply, System.StringComparison.Ordinal);
		int themed = src.IndexOf("var t = SpzUiThemeOps.Active;", leave, System.StringComparison.Ordinal);
		string body = src.Substring(leave, themed - leave);
		Assert.That(body, Does.Contain("RestoreCircle(_rembg_backgroundThresh)"));
		Assert.That(body, Does.Contain("RestoreCircle(_rembg_foregroundThresh)"));
	}

	[Test]
	public void CommandRibbon_ApiPassesShouldRecolorBoundChrome() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Layouts/RightPanel/CommandRibbon_UI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain(
			"ApplyStudioTabChromeColors(cell, SpzUiThemeOps.Active, SpzUiThemeOps.ShouldRecolorBoundChrome)"));
	}

	[Test]
	public void Art3DList_LeaveUsesRestoreBoundChromeUnder() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Generate/Art3D_IconsUI_List.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(transform)"));
	}

	[Test]
	public void SubMesh_LeaveRestoresRemoveButtonBoundChrome() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/3D Models/UI/SD_subMesh_IconUI.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_rmvButton.transform)"));
	}
}
