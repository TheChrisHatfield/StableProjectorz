using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ModelsHandlerMeshListLeaveThemeTests {
	[Test]
	public void MeshListLeave_RestoresCrossWiredImportButtons() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Models", "ModelsHandler_3D_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreChromeButton(_import_button)"));
		Assert.That(src, Does.Contain("RestoreChromeButton(_loadModel_button)"));
		Assert.That(src, Does.Contain("RestoreChromeButton(_import_andKeepIcons_button)"));
	}
}
