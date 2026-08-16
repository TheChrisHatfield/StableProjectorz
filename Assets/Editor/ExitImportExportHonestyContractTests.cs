using System;
using System.IO;
using NUnit.Framework;

public sealed class ExitImportExportHonestyContractTests {

	/// <summary>
	/// The block starting at the first '{' at or after <paramref name="from"/>, through its matching
	/// '}'. Fixed character windows silently slide off the code they meant to check as soon as a
	/// comment or log message grows, which is exactly how these assertions stopped meaning anything.
	/// </summary>
	static string BlockAt(string src, int from) {
		int open = src.IndexOf('{', from);
		Assert.That(open, Is.GreaterThanOrEqualTo(0), "expected a block after the anchor");
		int depth = 0;
		for (int i = open; i < src.Length; i++) {
			if (src[i] == '{') depth++;
			else if (src[i] == '}' && --depth == 0) return src.Substring(from, i - from + 1);
		}
		Assert.Fail("unbalanced braces after the anchor");
		return "";
	}

	[Test]
	public void Exit_BlocksQuitWhenPopupMissing() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "_Core", "System", "ExitTheProgram_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("ConfirmPopup_UI.instance==null", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0));
		string guard = BlockAt(src, i);
		Assert.That(guard, Does.Contain("return false"),
			"no popup means no \"save first?\" prompt, so quit must be refused");
		Assert.That(guard, Does.Not.Contain("OnExitConfirm()"),
			"the missing-popup guard must never quit outright");
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
		// Anchor on the definition, not the first mention: the earlier call site made this window four
		// lines of dispatch code that could never contain a method name.
		int i = src.IndexOf("bool DefersResponseUntilProjectSaveIdle(string method)", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThanOrEqualTo(0), "deferral predicate must exist");
		string body = BlockAt(src, i);
		Assert.That(body, Does.Contain("export_3d_with_textures\""));
		Assert.That(body, Does.Contain("export_3d_with_textures_to_path"));
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
