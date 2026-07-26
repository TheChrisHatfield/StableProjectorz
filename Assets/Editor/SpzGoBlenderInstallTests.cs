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
	}

	[Test]
	public void ShipBridge_InstallScriptExistsRelativeToProject() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "StreamingAssets", "Addons", "StableProjectorzGO", "BlenderBridge", "install_into_blender.py");
		Assert.That(File.Exists(path), Is.True, path);
	}
}
