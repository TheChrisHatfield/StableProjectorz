using System.IO;
using NUnit.Framework;

public sealed class SpzGoTextureSaveCancelClearsBusyTests {

	[Test]
	public void PathChosen_InvokesOnCompleteWhenPathEmpty() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("OnSaveViewTextures_PathChosen"));
		Assert.That(src, Does.Contain("OnSaveProjTextures_PathChosen"));
		// Both empty-path branches must invoke onComplete (not bare return).
		Assert.That(src, Does.Match(
			@"OnSaveViewTextures_PathChosen[\s\S]*?if\(string\.IsNullOrEmpty\(basePath\)\)\{\s*onComplete\?\.Invoke\(\);"));
		Assert.That(src, Does.Match(
			@"OnSaveProjTextures_PathChosen[\s\S]*?if\(string\.IsNullOrEmpty\(basePath\)\)\{\s*onComplete\?\.Invoke\(\);"));
	}
}
