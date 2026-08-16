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
	/// Unity LH Y-up → FBX RH Y-up is a z mirror only — the exact inverse of the importer's Assimp
	/// MakeLeftHanded. An extra axis rotation on the root (upstream wrote 90,-90,180) put exported
	/// figures on their side in Blender; mirroring x instead needed a 180° yaw on import, which
	/// flipped every Blender-authored model.
	/// </summary>
	[Test]
	public void ExportMirrorsZ_MatchingImport_NoBakedRotations() {
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
		Assert.That(src, Does.Contain("new FbxDouble3( -e.x, -e.y, e.z )"),
			"Euler must be mirrored for the -z handedness flip.");
		Assert.That(src, Does.Contain("-verts[v].z"),
			"Control points must mirror z, not x.");
		Assert.That(src, Does.Contain("if (basis.IsDefault) return parent;"),
			"Default settings must preserve the authored no-extra-root-transform path.");

		// The whole tangent frame shares the positions'/normals' handedness conversion. Mirroring only
		// part of it hands consumers a basis that disagrees with the surface, which reads as broken
		// tangent-space texturing — and gets worse under a mirroring export axis.
		Assert.That(src, Does.Contain("-normals[n][2]"),
			"Normals must mirror z.");
		Assert.That(src, Does.Contain("-binormals[n][2]"),
			"Binormals must mirror z with the normals, not stay in Unity handedness.");
		Assert.That(src, Does.Contain("-tangents[n][2]"),
			"Tangents must mirror z with the normals.");
		Assert.That(src, Does.Contain("-tangents[n][3]"),
			"Bitangent sign must invert: the z mirror reverses cross(normal, tangent).");

		string container = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "Objs3D_Container.cs");
		Assert.That(File.Exists(container), Is.True);
		string containerSrc = File.ReadAllText(container);
		Assert.That(containerSrc, Does.Not.Contain("Quaternion.Euler(0f, 180f, 0f)"),
			"Import must not yaw the model root — MakeLeftHanded already gives Unity orientation.");

		string loader = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "AssimpLoader.cs");
		Assert.That(File.Exists(loader), Is.True);
		Assert.That(File.ReadAllText(loader), Does.Contain("MakeLeftHanded"),
			"Export mirror axis is chosen to invert this import step.");

		string bridge = Path.Combine(
			Directory.GetCurrentDirectory(), "External", "Blender_SpzBridge", "__init__.py");
		Assert.That(File.Exists(bridge), Is.True);
		string bridgeSrc = File.ReadAllText(bridge);
		Assert.That(bridgeSrc, Does.Contain("_FBX_AXIS_UP = \"Y\""),
			"Blender side must read the FBX as Y-up to convert it to Blender Z-up.");
	}
}
