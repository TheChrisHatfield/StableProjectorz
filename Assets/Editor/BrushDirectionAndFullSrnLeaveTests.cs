using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class BrushDirectionAndFullSrnLeaveTests {
	[Test]
	public void DirectionGaps_SnapshotsLeOnNomad_And_DoesNotHardcode210OnLeave_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI",
			"SD_BrushRibbon_UI_Direction.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SnapshotLayoutElementForTheme(rootLayout)"));
		Assert.That(src, Does.Contain("if (rootLayout != null && nomadGaps)"));
		Assert.That(src, Does.Not.Contain("nomadGaps ? squareStackH : 210f"));
	}

	[Test]
	public void FlatToolColorBlock_SnapshotsBeforeWhite_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "BrushRibbon_UI", "BrushRibbon_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ApplyFlatToolColorBlock", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(500, src.Length - idx));
		Assert.That(body, Does.Contain("SnapshotAuthoredColorBlock(sel)"));
	}

	[Test]
	public void FullSrn_HideCornerTriangles_UnwindsHiddenGraphic_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion",
			"WorkflowToolsRibbon SD", "RibbonViewportFullViewOnScreen_Toggle_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void HideCornerTrianglesUnder", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("SpzUiThemeHiddenGraphic"));
		Assert.That(body, Does.Contain("tag.wasEnabled"));
	}
}
