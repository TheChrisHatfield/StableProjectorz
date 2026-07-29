using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Pass 23: mesh list row select must Ensure hit face under Nomad (basics / 3D gen path).
/// </summary>
public sealed class BoundChromePass23SubMeshRowHitFaceTests {

	[Test]
	public void SubMeshIcon_SourceEnsuresWholeIconButtonBeforeNameTmp() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/3D Models/UI/SD_subMesh_IconUI.cs"));
		string src = File.ReadAllText(path);
		int ensure = src.IndexOf("EnsureSelectableHitFace(_wholeIcon_button)", System.StringComparison.Ordinal);
		int nameTmp = src.IndexOf("ApplyBoundChromeTmp(_name", System.StringComparison.Ordinal);
		Assert.That(ensure, Is.GreaterThan(0));
		Assert.That(nameTmp, Is.GreaterThan(ensure),
			"Ensure whole-row face before name TMP clears label raycasts");
		Assert.That(src, Does.Contain("RestoreBoundChromeUnder(_wholeIcon_button.transform)"));
		Assert.That(src, Does.Contain("ClearNonFaceRaycastsForTheme(_wholeIcon_button)"));
	}
}
