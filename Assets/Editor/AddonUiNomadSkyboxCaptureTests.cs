using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>Nomad skybox capture must not treat a missing SkyboxBackground_MGR as Color.clear.</summary>
public sealed class AddonUiNomadSkyboxCaptureTests {

	[Test]
	public void CaptureNomadSkyboxIfNeeded_DoesNotMarkCapturedWhenSkyboxNull() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonUI_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void CaptureNomadSkyboxIfNeeded()", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("void ComposeNomadSkyboxNative()", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("if (skybox == null)"));
		Assert.That(body, Does.Contain("return;"),
			"Must return without setting _nomadSkyboxCaptured when skybox mgr is missing.");
		Assert.That(body, Does.Not.Contain("_nomadSkyboxTopBefore = Color.clear"),
			"Clear snapshot would wipe the live skybox on Restore.");
	}
}
