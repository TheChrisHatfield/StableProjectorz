using System.IO;
using NUnit.Framework;

/// <summary>
/// Gen3D image drag-drop hit-test must pass canvas camera on non-overlay canvases.
/// </summary>
public sealed class Gen3DImageDragDropContractTests {

	static string RepoPath(params string[] parts) {
		return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts)));
	}

	[Test]
	public void ImageInputs_HitTestUsesUiCameraFor() {
		string path = RepoPath(
			"Assets", "_gm", "Features", "3D Generate", "Gen3D_InputPanelBuilder_UI",
			"Gen3D_All_ImageInputs_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("OnDragAndDropImages"));
		Assert.That(src, Does.Contain("UiCameraFor"));
		Assert.That(src, Does.Contain("RectangleContainsScreenPoint"));
		Assert.That(src, Does.Contain("screenCoord, cam"));
		Assert.That(src, Does.Contain("RenderMode.ScreenSpaceOverlay"));
	}
}
