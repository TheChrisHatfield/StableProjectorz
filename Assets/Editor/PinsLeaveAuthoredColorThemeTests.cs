using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PinsLeaveAuthoredColorThemeTests {
	[Test]
	public void PinsLeave_ReassertsAuthoredPinPlateColor() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Camera", "Navigation", "CamerasMGR_PinsZone_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("rootImg.color = new Color(_pinColor.r"));
	}
}
