using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class Icon3DAndConnectionLeaveThemeTests {
	[Test]
	public void Icon3D_ContextThemesGenExportOutsideMenuRoot() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Icon3D_ContextMenu.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeOrRestoreGenExportButton(_generateButton)"));
		Assert.That(src, Does.Contain("ThemeOrRestoreGenExportButton(_exportMeshButton)"));
	}

	[Test]
	public void ConnectionPanel_LeaveRestoresOpenAndResetButtons() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Connection", "ConnectionPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_openPanel_button.transform)"));
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_resetToDefault_button.transform)"));
	}
}
