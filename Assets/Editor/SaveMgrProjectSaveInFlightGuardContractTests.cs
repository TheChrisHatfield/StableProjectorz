using System.IO;
using NUnit.Framework;

/// <summary>
/// Save Project dialog sets IsProjectSaveInFlight before _isSaving — Load/Export must refuse that window.
/// </summary>
public sealed class SaveMgrProjectSaveInFlightGuardContractTests {

	[Test]
	public void DoLoadProject_RefusesProjectSaveInFlight() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("IsProjectSaveDialogOrWriteInFlight"));
		int i = src.IndexOf("public void DoLoadProject()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("IsProjectSaveDialogOrWriteInFlight()"));
	}

	[Test]
	public void ExportAndTextureSaves_RefuseProjectSaveInFlight() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Export3D_with_textures: refused"));
		int export = src.IndexOf("public bool Export3D_with_textures()", System.StringComparison.Ordinal);
		string exportBody = src.Substring(export, System.Math.Min(500, src.Length - export));
		Assert.That(exportBody, Does.Contain("IsProjectSaveDialogOrWriteInFlight()"));
		Assert.That(src, Does.Contain("SaveViewTextures"));
		int view = src.IndexOf("public void SaveViewTextures()", System.StringComparison.Ordinal);
		string viewBody = src.Substring(view, System.Math.Min(400, src.Length - view));
		Assert.That(viewBody, Does.Contain("IsProjectSaveDialogOrWriteInFlight()"));
	}

	[Test]
	public void MergeIcons_RefusesWhenBusyBeforeClaimingFlag() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void MergeIcons(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string body = src.Substring(i, System.Math.Min(700, src.Length - i));
		Assert.That(body, Does.Contain("IsProjectSaveDialogOrWriteInFlight()"));
		Assert.That(body.IndexOf("if( _isSaving || IsProjectSaveDialogOrWriteInFlight()", System.StringComparison.Ordinal),
			Is.LessThan(body.IndexOf("_isSaving = true", System.StringComparison.Ordinal)));
	}
}
