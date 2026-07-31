using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>BakeAO early-outs must always invoke onBakeComplete so AO Stop UI can reset.</summary>
public sealed class AmbientOcclusionBakeCompleteCallbackTests {

	[Test]
	public void BakeAO_ImportingModelGuard_InvokesOnBakeComplete() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/TextureTools/AO/AmbientOcclusion_Baker.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int importing = src.IndexOf("_isImportingModel", System.StringComparison.Ordinal);
		Assert.That(importing, Is.GreaterThan(0));
		string window = src.Substring(importing, Math.Min(280, src.Length - importing));
		Assert.That(window, Does.Contain("onBakeComplete?.Invoke(false)"),
			"Importing-model early return must invoke onBakeComplete or Bake/Stop buttons stick.");
	}
}
