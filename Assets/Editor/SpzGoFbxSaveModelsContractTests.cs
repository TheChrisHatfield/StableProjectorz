using System.IO;
using NUnit.Framework;

/// <summary>
/// FBX writer must not leave a stale on-disk file counting as a successful export.
/// </summary>
public sealed class SpzGoFbxSaveModelsContractTests {

	[Test]
	public void SaveModels_ReturnsBoolAndClearsPriorFile() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public bool SaveModels("),
			"SaveModels must return bool so callers can fail closed.");
		Assert.That(src, Does.Contain("File.Delete(finalFilepath_with_exten)"),
			"Prior FBX must be removed so a failed Initialize cannot look like success.");
		Assert.That(src, Does.Contain("return false;"),
			"Initialize/Export failures must return false.");
	}

	/// <summary>
	/// Unity LH Y-up → FBX RH Y-up is an x mirror only. An extra axis rotation on the model root
	/// (upstream wrote 90,-90,180) put exported figures on their side in Blender.
	/// </summary>
	[Test]
	public void ExportTransform_MirrorsXOnly_NoBakedRootAxisRotation() {
		string helper = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler_SaveFBX_Helper.cs");
		Assert.That(File.Exists(helper), Is.True);
		string src = File.ReadAllText(helper);
		Assert.That(src, Does.Not.Contain("new Vector3(90, -90, 180)"),
			"Root must not bake an axis rotation — Blender's Y-up import would tip the model over.");
		Assert.That(src, Does.Not.Contain("rotAdjust"),
			"Root and children must share one transform path (no root-only rotation adjust).");
		Assert.That(src, Does.Contain("Vector3 e = tr.localEulerAngles"),
			"Rotations must be exported as Euler degrees, not quaternion components.");
		Assert.That(src, Does.Contain("new FbxDouble3( e.x, -e.y, -e.z )"),
			"Euler must be mirrored for the -x handedness flip.");

		string bridge = Path.Combine(
			Directory.GetCurrentDirectory(), "External", "Blender_SpzBridge", "__init__.py");
		Assert.That(File.Exists(bridge), Is.True);
		string bridgeSrc = File.ReadAllText(bridge);
		Assert.That(bridgeSrc, Does.Contain("_FBX_AXIS_UP = \"Y\""),
			"Blender side must read the FBX as Y-up to convert it to Blender Z-up.");
	}
}
