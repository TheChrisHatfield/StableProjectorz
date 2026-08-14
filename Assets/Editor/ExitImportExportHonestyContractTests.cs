using System;
using System.IO;
using NUnit.Framework;

public sealed class ExitImportExportHonestyContractTests {

	[Test]
	public void Exit_BlocksQuitWhenPopupMissing() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "System", "ExitTheProgram_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("ConfirmPopup_UI.instance==null", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string snip = src.Substring(i, Math.Min(280, src.Length - i));
		Assert.That(snip, Does.Contain("return false"));
		Assert.That(snip, Does.Not.Contain("OnExitConfirm()"));
	}

	[Test]
	public void Exit_AbortsStuckConfirmThenShowsClosePrompt() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "System", "ExitTheProgram_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("AbortPendingUninstallConfirm"),
			"Quit must stop deferred Uninstall Show before Abort/Show Exit.");
		Assert.That(src, Does.Contain("IsCloseProgramPrompt"),
			"Force-quit after repeated X must only apply while Exit prompt is still showing.");
		Assert.That(src, Does.Contain("ForceQuitAfterCloseAttempts"),
			"Repeated window-close while Exit prompt is up must force quit if Yes/Close is unresponsive.");
		Assert.That(src, Does.Not.Contain("another confirm is already open"),
			"Must not return false solely because another confirm is open (that locked the app).");
	}

	[Test]
	public void ExportRpc_DefersDialogExportsUntilSaveIdle() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_SocketServer.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("DefersResponseUntilProjectSaveIdle", System.StringComparison.Ordinal);
		int j = src.IndexOf("DefersResponseUntilImportIdle", i + 10, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("export_3d_with_textures\""));
		Assert.That(body, Does.Contain("export_projection_textures"));
		Assert.That(body, Does.Contain("export_view_textures"));
	}

	[Test]
	public void ImportExisting_OnException_CallsOnFail() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Images_ImportHelper.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void OnImport_ExistingImages(", System.StringComparison.Ordinal);
		int j = src.IndexOf("void OnImportCustomImage_FileConfirmed", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Contain("onFail?.Invoke"));
		Assert.That(body, Does.Contain("return;"));
	}
}
