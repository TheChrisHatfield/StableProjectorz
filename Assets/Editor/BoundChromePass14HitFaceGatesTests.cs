using System.IO;
using NUnit.Framework;
using spz;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass 14: gen/basics surfaces must theme via ApplyBoundChromeSelectable/Ensure —
/// not skip when prefab Button.targetGraphic is null (Nomad clears label raycasts).
/// </summary>
public sealed class BoundChromePass14HitFaceGatesTests {

	[Test]
	public void GenerateCancelDelete_SourceDropsNullTargetGraphicGate() {
		string src = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Layouts/Viewport (MainView)/GenerateButtons_Main_UI.cs")));
		Assert.That(src, Does.Not.Contain(
			"_cancelGeneration_button != null && _cancelGeneration_button.targetGraphic != null"));
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(_cancelGeneration_button"));
		Assert.That(src, Does.Contain("ApplyBoundChromeSelectable(delBtn"));
		Assert.That(src, Does.Not.Contain("delBtn != null && delBtn.targetGraphic != null"));
	}

	[Test]
	public void MultiviewConnectionPaintBrush_SourcesDropNullTargetGraphicGates() {
		string mv = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Camera/Multi-View/MultiView_Ribbon_UI.cs")));
		Assert.That(mv, Does.Not.Contain(
			"_BlendCams_button != null && _BlendCams_button.targetGraphic != null"));
		Assert.That(mv, Does.Not.Contain("sortBtn != null && sortBtn.targetGraphic != null"));

		string conn = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Connection/ConnectionPanel_UI.cs")));
		Assert.That(conn, Does.Not.Contain(
			"_openPanel_button != null && _openPanel_button.targetGraphic != null"));
		Assert.That(conn, Does.Not.Contain(
			"_resetToDefault_button != null && _resetToDefault_button.targetGraphic != null"));

		string paint = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Paint/PaintTab/PaintTab_CollectPaintUI.cs")));
		int owned = paint.IndexOf("ThemeOwnedSection", System.StringComparison.Ordinal);
		Assert.That(owned, Is.GreaterThan(0));
		string body = paint.Substring(owned, System.Math.Min(2200, paint.Length - owned));
		Assert.That(body, Does.Not.Contain("btn.targetGraphic == null) continue"));
		Assert.That(body, Does.Not.Contain("toggle.targetGraphic == null) continue"));

		string brush = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Paint/BrushRibbon_UI/BrushRibbon_UI.cs")));
		int themeTool = brush.IndexOf("static void ThemeToolButton", System.StringComparison.Ordinal);
		Assert.That(themeTool, Is.GreaterThan(0));
		string toolBody = brush.Substring(themeTool, System.Math.Min(600, brush.Length - themeTool));
		Assert.That(toolBody, Does.Contain("ApplyBoundChromeSelectable(btn"));
		Assert.That(toolBody, Does.Not.Contain("if (btn.targetGraphic != null)"));
	}

	[Test]
	public void ExportMeshIconsGen3D_SourcesDropNullTargetGraphicGates() {
		string export = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Save Load Import Export/ExportSave_UI_MGR.cs")));
		Assert.That(export, Does.Contain("static void ThemeMenuButton"));
		Assert.That(export, Does.Not.Contain("btn == null || btn.targetGraphic == null) return"));

		string mesh = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/3D Models/ModelsHandler_3D_UI.cs")));
		Assert.That(mesh, Does.Not.Contain("btn == null || btn.targetGraphic == null) return"));
		Assert.That(mesh, Does.Not.Contain("btn != null && btn.targetGraphic != null"));

		string gen3d = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/3D Generate/Generation3D_Prompt_UI.cs")));
		Assert.That(gen3d, Does.Not.Contain("toggle == null || toggle.targetGraphic == null) continue"));

		string rembg = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/3D Generate/Gen3D_WorkflowOptionsRibbon_UI.cs")));
		Assert.That(rembg, Does.Not.Contain(
			"_rembg_button != null && _rembg_button.targetGraphic != null"));

		string icons = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath, "_gm/Features/Icons/IconUI_List_Art/IconsUI_List.cs")));
		Assert.That(icons, Does.Not.Contain("btn == null || btn.targetGraphic == null) continue"));

		string dock = File.ReadAllText(Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/StableDiffusion/WorkflowToolsRibbon SD/RibbonViewportFullViewOnScreen_Toggle_UI.cs")));
		Assert.That(dock, Does.Not.Contain("openBtn != null && openBtn.targetGraphic != null"));
	}

	[Test]
	public void EnsureSelectableHitFace_CreatesFaceWhenNull() {
		var go = new GameObject("Pass14HitFace", typeof(RectTransform), typeof(Button));
		try {
			var btn = go.GetComponent<Button>();
			btn.targetGraphic = null;
			var face = SpzUiThemeOps.EnsureSelectableHitFace(btn);
			Assert.That(face, Is.Not.Null);
			Assert.That(btn.targetGraphic, Is.SameAs(face));
			Assert.That(face.raycastTarget, Is.True);
		} finally {
			Object.DestroyImmediate(go);
		}
	}
}
