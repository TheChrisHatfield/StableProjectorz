using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class MeshListGlyphChromeThemeTests {
	[Test]
	public void ModelsHandler_ThemeChromeButtonSkipsAuthoredIconFaces() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Models", "ModelsHandler_3D_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsAuthoredIconFace(btn.targetGraphic)"));
		Assert.That(src, Does.Contain("ApplyBoundChromeGraphic(face, t.iconTint)"));
		Assert.That(src, Does.Contain("EnsureSelectableHitFace(btn)"));
		Assert.That(src, Does.Contain("CommandRibbon_UI — skip dual root tint"));
	}
}

public sealed class LayerVisibilityFaceOwnerThemeTests {
	[Test]
	public void LayersPanel_WiresAuthoredVisibilityFaceWithoutClearNonFace() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_LayersPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int ix = src.IndexOf("var vis = row.transform.Find(\"Visibility\")");
		Assert.That(ix, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(ix, System.Math.Min(900, src.Length - ix));
		Assert.That(body, Does.Contain("visBtn.targetGraphic = authored"));
		Assert.That(body, Does.Not.Contain("ClearNonFaceRaycastsForTheme(visBtn)"));
	}
}

public sealed class ArtListPanelShellOwnerThemeTests {
	[Test]
	public void ArtList_DoesNotDualThemeRootPanelShell() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Icons", "IconUI_List_Art", "IconsUI_List.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CommandRibbon_UI.RecolorOrRestorePanelShell"));
		Assert.That(src, Does.Not.Contain("ApplyBoundChromeGraphic(rootImg, t.panelBg)"));
	}
}
