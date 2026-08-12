using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class DoubleClickConfirmRaycastThemeTests {
	[Test]
	public void DoubleClick_EnablesConfirmTextRaycastWhileFaceHidden() {
		string path = Path.Combine(Application.dataPath, "_gm", "_Core", "UI (reusable)",
			"Widgets and Gadgets", "Buttons Toggles", "DoubleClickButton_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("_text.raycastTarget = true"));
		Assert.That(src, Does.Contain("Nomad ClearNonFace"));
	}
}
