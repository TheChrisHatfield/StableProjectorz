using System.IO;
using NUnit.Framework;

/// <summary>
/// Icon Save2DArt must refuse while _isSaving — same race SaveViewTextures already guards.
/// </summary>
public sealed class Save2DArtBusyGuardTests {

	[Test]
	public void Save2DArt_RefusesWhileBusyLikeViewTextures() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);

		int art = src.IndexOf("public void Save2DArt(", System.StringComparison.Ordinal);
		Assert.That(art, Is.GreaterThan(0));
		int next = src.IndexOf("public void SaveViewTextures(", art, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(art));
		string body = src.Substring(art, next - art);
		Assert.That(body, Does.Contain("if( _isSaving )"),
			"Save2DArt must refuse when another save/export owns _isSaving.");
		Assert.That(body, Does.Contain("Can't save icon while a save/export is still writing"),
			"Busy refuse must surface a status toast.");

		int exact = src.IndexOf("public void Save2DArt_ExactPath(", System.StringComparison.Ordinal);
		Assert.That(exact, Is.GreaterThan(0));
		string exactBody = src.Substring(exact, art - exact);
		Assert.That(exactBody, Does.Contain("if( _isSaving )"),
			"Save2DArt_ExactPath must not clear _isSaving mid-export.");
	}
}
