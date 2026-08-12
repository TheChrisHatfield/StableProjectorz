using System.IO;
using NUnit.Framework;

public sealed class AddonSpzGoNativeParityAndRememberPersistContractTests {

	[Test]
	public void NativeSpzGo_SeedsAutofillRefreshExportDialogPrintDataDir() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("do_autofill_mesh_paths"));
		Assert.That(src, Does.Contain("do_refresh_blender_path"));
		Assert.That(src, Does.Contain("do_export_interactive"));
		Assert.That(src, Does.Contain("do_show_data_dir"));
		Assert.That(src, Does.Contain("SpzGoNativeAutofillPaths"));
	}

	[Test]
	public void PersistEnabled_ClearsJsonWhenRememberOff() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "Addon_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void PersistEnabledAddonSelectionNow()", System.StringComparison.Ordinal);
		string body = src.Substring(i, System.Math.Min(500, src.Length - i));
		Assert.That(body, Does.Contain("GetRememberEnabledAddonsPreference()"));
		Assert.That(body, Does.Contain("DeleteKey(PrefsKeyEnabledAddonIdsJson)"));
	}
}
