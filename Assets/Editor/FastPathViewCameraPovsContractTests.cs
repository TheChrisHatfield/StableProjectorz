using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Multiview Manager addon RPCs: POV snapshot, isolate, restore, per-slot apply.</summary>
public sealed class FastPathViewCameraPovsContractTests {

	[Test]
	public void FastPath_ExposesMultiviewPovSnapshotAndRestore() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/FastPath_API.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public JObject GetViewCameraPovsJson()"));
		Assert.That(src, Does.Contain("public bool RestoreViewCameraPovsFromJson(JArray povsArray)"));
		Assert.That(src, Does.Contain("public bool IsolateViewCameraRpc(int index)"));
		Assert.That(src, Does.Contain("public bool ApplyViewCameraSlotPovRpc(int index, JObject pov)"));
		Assert.That(src, Does.Contain("mgr.Restore_CamerasPlacements(dummy)"));
		Assert.That(src, Does.Contain("[\"index\"]"));
		Assert.That(src, Does.Contain("anyParsed"));
	}

	[Test]
	public void UserCamerasMgr_ExposesTryIsolateViewCamera() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Camera/UserCameras_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public bool TryIsolateViewCamera(int index)"));
	}

	[Test]
	public void SocketServer_WiresMultiviewPovRpcs() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/Addon_SocketServer.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("spz.cmd.get_view_camera_povs"));
		Assert.That(src, Does.Contain("spz.cmd.isolate_view_camera"));
		Assert.That(src, Does.Contain("spz.cmd.restore_view_camera_povs"));
		Assert.That(src, Does.Contain("spz.cmd.apply_view_camera_slot_pov"));
	}

	[Test]
	public void MultiviewManagerAddon_IsRegisteredUnderStreamingAssets() {
		string initPy = Path.GetFullPath(Path.Combine(
			Application.dataPath, "StreamingAssets", "Addons", "MultiviewManagerSPZ", "__init__.py"));
		Assert.That(File.Exists(initPy), Is.True, initPy);
		string src = File.ReadAllText(initPy);
		Assert.That(src, Does.Contain("MultiviewManagerSPZ"));
		Assert.That(src, Does.Contain("get_povs"));
		Assert.That(src, Does.Contain("slot_bookmarks.json"));
		Assert.That(src, Does.Contain("presets.json"));
	}
}
