using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>SetCameraFOV must update fovMgr._trueCameraFov, not only myCamera.fieldOfView.</summary>
public sealed class FastPathSetCameraFovContractTests {

	[Test]
	public void SetCameraFOV_UsesFovMgrSetFieldOfView() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/FastPath_API.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("public bool SetCameraFOV(int cameraIndex, float fov)", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("public JObject GetViewCamerasStateJson()", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("fovMgr.SetFieldOfView"),
			"SetCameraFOV must call fovMgr.SetFieldOfView so GetCameraFOV sees the new value.");
		Assert.That(body, Does.Contain("camera.fovMgr != null"));
	}
}
