using System.IO;
using NUnit.Framework;

/// <summary>
/// Final-composite restart must not StopCoroutine an in-flight headless export texture pipeline,
/// but must clear a stale coroutine handle so a new export is not orphaned with _isSaving stuck true.
/// Refuse must return false so callers that already claimed _isSaving can clear it.
/// </summary>
public sealed class SpzGoFinalCompositeBusyGuardTests {

	[Test]
	public void SaveFinalComposite_RefusesRestartWhileActivelySaving() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "ProjectSaveLoad_Helper.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("public bool Save_FinalCompositeTexture"),
			"Must return bool so export callers can clear _isSaving on refuse.");
		Assert.That(src, Does.Contain("_finalCompositeActive"),
			"Must track whether the final-composite coroutine is actually running.");
		Assert.That(src, Does.Contain("sm._isSaving && _finalCompositeActive"),
			"Refuse restart only when an active composite owns the in-progress export.");
		Assert.That(src, Does.Contain("Cleared stale final-composite handle"),
			"Stale non-null coroutine refs must be cleared so Export OnReady is not skipped.");
		Assert.That(src, Does.Contain("Refusing to restart final-composite while a save/export is in progress"),
			"Live in-flight composite must still refuse StopCoroutine during Save_MGR._isSaving.");
		Assert.That(src, Does.Contain("return false"),
			"Busy refuse must return false (not void early-return that orphans _isSaving).");
	}

	[Test]
	public void ExportCallers_ClearIsSavingWhenFinalCompositeRefused() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Save Load Import Export", "Save_MGR.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("!_saveLoad_helper.Save_FinalCompositeTexture"),
			"Export/MergeIcons must check Save_FinalCompositeTexture success.");
		Assert.That(src, Does.Contain("busy composing textures"),
			"Headless/dialog export must surface refuse and clear _isSaving.");
	}
}
