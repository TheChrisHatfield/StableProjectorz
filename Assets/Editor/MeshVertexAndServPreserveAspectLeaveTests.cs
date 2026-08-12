using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class MeshVertexAndServPreserveAspectLeaveTests {
	[Test]
	public void VertexColorsToggle_LeaveRestores_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Models", "ModelsHandler_3D_UI.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("void ThemeVertexColorsToggle", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(800, src.Length - idx));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder"));
		Assert.That(body, Does.Not.Contain("|| !SpzUiThemeOps.ShouldRecolorBoundChrome) return;"));
	}

	[Test]
	public void RestartWebui_DoesNotBypassPreserveAspectRestore_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Webui", "RestartTheWebui.cs");
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void ThemeTopStripButton", System.StringComparison.Ordinal);
		string body = src.Substring(idx, System.Math.Min(900, src.Length - idx));
		Assert.That(body, Does.Contain("FlattenToolFaceImage(face)"));
		Assert.That(body, Does.Not.Contain("face.preserveAspect = false"));
	}
}
