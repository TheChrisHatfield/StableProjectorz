using System.IO;
using NUnit.Framework;

/// <summary>
/// GO FBX export must undo SPZ fit-to-volume so Blender default-cube litmus (~2m) round-trips.
/// </summary>
public sealed class SpzGoExportAuthoringScaleTests {

	[Test]
	public void SaveDefaultDoor_UndoesFitScaleAroundFbxWrite() {
		string helper = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "ModelsHandler3D_ImportHelper.cs");
		string container = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "3D Models", "Objs3D_Container.cs");
		string save = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(helper), Is.True);
		Assert.That(File.Exists(container), Is.True);
		string helperSrc = File.ReadAllText(helper);
		string containerSrc = File.ReadAllText(container);
		string saveSrc = File.ReadAllText(save);
		Assert.That(containerSrc, Does.Contain("TryBeginFbxExportAuthoringScale"),
			"Container must expose temporary authoring-scale export.");
		Assert.That(containerSrc, Does.Contain("BlenderDefaultCubeEdgeMeters"),
			"Litmus constant for Blender default cube must be documented.");
		Assert.That(containerSrc, Does.Contain("SpzFitTargetMaxDimension"),
			"SPZ fit target must be a named constant (not a magic 3.0 only).");
		Assert.That(helperSrc, Does.Contain("TryBeginFbxExportAuthoringScale"),
			"FBX SaveDefaultDoor must undo fit before writing.");
		Assert.That(helperSrc, Does.Contain("EndFbxExportAuthoringScale"),
			"FBX SaveDefaultDoor must restore viewport scale after writing.");
		Assert.That(saveSrc, Does.Contain("scale_undid_fit=1"),
			"Ready stamp must tell Blender the fit was already undone.");
	}
}
