using System.IO;
using NUnit.Framework;

/// <summary>
/// Camera nav must not NRE when MainViewport_UI.instance is missing (boot / unload).
/// </summary>
public sealed class CameraNavViewportNullGuardContractTests {

	[Test]
	public void CameraNav_Sources_UseNullConditionalHover() {
		string[] rel = {
			Path.Combine("Camera", "Navigation", "CameraMove.cs"),
			Path.Combine("Camera", "Navigation", "CameraOrbit.cs"),
			Path.Combine("Camera", "Navigation", "CameraPanning.cs"),
			Path.Combine("Camera", "Navigation", "CameraDolly.cs"),
			Path.Combine("Camera", "Navigation", "CameraFocus.cs"),
			Path.Combine("Camera", "Navigation", "Camera_UV_NavigateHelper.cs"),
		};
		foreach (string r in rel) {
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "_gm", "Features", r);
			Assert.That(File.Exists(path), Is.True, path);
			string src = File.ReadAllText(path);
			Assert.That(src, Does.Contain("MainViewport_UI.instance?.isCursorHoveringMe()"), path);
			Assert.That(src, Does.Not.Contain("MainViewport_UI.instance.isCursorHoveringMe()"),
				"unguarded hover in " + r);
		}
	}
}
