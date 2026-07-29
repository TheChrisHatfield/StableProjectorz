using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using spz;

/// <summary>
/// Guards SPZ GO Blender bridge install FastPath surface + native callback wiring.
/// </summary>
public sealed class SpzGoBlenderInstallTests {

	[Test]
	public void FastPath_ExposesBlenderBridgeInstallApi() {
		Assert.That(
			typeof(FastPath_API).GetMethod("TryInstallSpzGoBlenderBridge", BindingFlags.Instance | BindingFlags.Public),
			Is.Not.Null);
		Assert.That(
			typeof(FastPath_API).GetMethod("GetSpzGoBlenderBridgeShipDir", BindingFlags.Static | BindingFlags.Public),
			Is.Not.Null);
		Assert.That(
			typeof(FastPath_API).GetMethod("FindBlenderExecutable", BindingFlags.Static | BindingFlags.Public),
			Is.Not.Null,
			"Native Install must auto-find blender.exe when the path field is empty.");
	}

	[Test]
	public void AddonUiMgr_NativeInstallCallbackWired() {
		var method = typeof(AddonUI_MGR).GetMethod(
			"SpzGoNativeInstallBlenderBridge",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(method, Is.Not.Null, "Native Install into Blender must call SpzGoNativeInstallBlenderBridge.");
		var co = typeof(AddonUI_MGR).GetMethod(
			"CoSpzGoNativeInstallBlenderBridge",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.That(co, Is.Not.Null, "Install must run off the UI thread via CoSpzGoNativeInstallBlenderBridge.");
	}

	[Test]
	public void ShipBridge_InstallScriptExistsRelativeToProject() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "StreamingAssets", "Addons", "StableProjectorzGO", "BlenderBridge", "install_into_blender.py");
		Assert.That(File.Exists(path), Is.True, path);
	}

	[Test]
	public void TryInstall_UsesIl2CppSafeProcessLaunch() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "FastPath_API.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int start = src.IndexOf("public bool TryInstallSpzGoBlenderBridge", StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0));
		int end = src.IndexOf("public bool ExportProjectionTextures", start, StringComparison.Ordinal);
		Assert.That(end, Is.GreaterThan(start));
		string body = src.Substring(start, end - start);
		Assert.That(body, Does.Not.Contain("Process.Start"),
			"IL2CPP asserts on System.Diagnostics.Process.Start (CreateProcess_internal).");
		Assert.That(body, Does.Not.Contain("ProcessStartInfo"),
			"Must not use ProcessStartInfo under IL2CPP player builds.");
		Assert.That(body, Does.Contain("StartExternalProcess"),
			"Must launch blender via Win32 CreateProcess (StartExternalProcess).");
		Assert.That(body, Does.Contain(".bat"),
			"Temp .bat + log redirect captures install markers without stdout pipes.");
		Assert.That(body, Does.Contain("KillProcessTree"),
			"Timeout must kill cmd+blender tree (KillProcess alone orphans blender).");
		Assert.That(body, Does.Contain("UTF8Encoding(encoderShouldEmitUTF8Identifier: false)")
			.Or.Contain("new UTF8Encoding(false)"),
			"Temp .bat must be written without UTF-8 BOM (BOM breaks cmd.exe).");
		Assert.That(body, Does.Contain("SPZ_GO_INSTALL_OK"));
	}
}
