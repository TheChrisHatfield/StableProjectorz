using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class SdInputPanelLeaveThemeTests {
	[Test]
	public void SdInputPanel_LeaveRestoresResolutionPresets() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Input Panel", "SD_InputPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestorePreset(_resolutionPreset_512)"));
		Assert.That(src, Does.Contain("RestorePreset(_resolutionPreset_2048)"));
	}
}
