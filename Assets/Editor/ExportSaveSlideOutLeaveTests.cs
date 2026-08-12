using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class ExportSaveSlideOutLeaveTests {
	[Test]
	public void ExportSave_LeaveRestoresOptionsSlideOut_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Save Load Import Export", "ExportSave_UI_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_options_slideOut.transform)"));
		Assert.That(src, Does.Contain("ApplyBoundChromeGraphic(panelImg, t.panelBg)"));
	}
}
