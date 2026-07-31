using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>MakeUniquePath must not silently overwrite existing texture files.</summary>
public sealed class SaveMgrMakeUniquePathContractTests {

	[Test]
	public void MakeUniquePath_ChecksFileExistsAndIncrements() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Save Load Import Export/Save_MGR.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("string MakeUniquePath(string basePath, string suffix)", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("IEnumerator WaitForRenderAll_crtn", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("File.Exists(candidate)"),
			"MakeUniquePath must check existence before returning.");
		Assert.That(body, Does.Contain("for (int n = 2"),
			"Colliding paths must increment a numeric suffix.");
	}
}
