using System.IO;
using NUnit.Framework;

/// <summary>
/// Save2DArt cancel (empty filepath) must still Destroy disposable Texture2Ds when destroyTexs is true.
/// </summary>
public sealed class Save2DArtCancelDisposesTexturesContractTests {

	[Test]
	public void OnBasePathChosen_EmptyPath_DestroysWhenDestroyTexs() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("void OnBasePathForTextures_Chosen(", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThanOrEqualTo(0));
		int next = src.IndexOf("string MakeUniquePath(", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("string.IsNullOrEmpty(filepath)"));
		Assert.That(body, Does.Contain("destroyTexs && saveMe != null"));
		Assert.That(body, Does.Contain("DestroyImmediate(kvp.Key)"));
	}
}
