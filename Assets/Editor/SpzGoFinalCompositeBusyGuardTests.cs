using System.IO;
using NUnit.Framework;

/// <summary>
/// Final-composite restart must not StopCoroutine an in-flight headless export texture pipeline.
/// </summary>
public sealed class SpzGoFinalCompositeBusyGuardTests {

	[Test]
	public void SaveFinalComposite_RefusesRestartWhileIsSaving() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("Refusing to restart final-composite while a save/export is in progress"),
			"Save_FinalCompositeTexture must not StopCoroutine during Save_MGR._isSaving.");
		Assert.That(src, Does.Contain("sm._isSaving"),
			"Busy check must read Save_MGR._isSaving.");
	}
}
