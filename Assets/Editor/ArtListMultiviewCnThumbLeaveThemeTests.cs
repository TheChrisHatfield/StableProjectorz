using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ArtListMultiviewCnThumbLeaveThemeTests {
	[Test]
	public void IconsUI_List_LeaveRestoresHeaderScrollContainer() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Icons", "IconUI_List_Art", "IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_header.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_sr_itemFocuser.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_container)"));
	}

	[Test]
	public void Multiview_LeaveRestoresBlendGridPovSort() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Multi-View", "MultiView_Ribbon_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_BlendCams_button.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_sortPins_Button.transform)"));
		Assert.That(src, Does.Contain("ApplyBoundChromeReadableBodyTmp(gLabel"));
		Assert.That(src, Does.Contain("ApplyBoundChromeReadableBodyTmp(tmp, t.textPrimary, 11f)"));
	}

	[Test]
	public void ControlNetThumb_LeaveReappliesDialSelfSilo() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Controlnet", "ControlNetUnit_Thumb_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_depthContrast_slider.ApplyThemeTokens"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_closeButton.transform)"));
	}

	[Test]
	public void Art3D_List_LeaveRestoresDraggableGrid() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Art3D_IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_draggableItemsGrid.transform)"));
	}
}
