using System.IO;
using NUnit.Framework;

/// <summary>
/// Main/Mini subscribe to GenerateButtons_UI static generate buses in Awake. Without matching
/// unsubscribe on destroy, reload/domain reload keeps dead handlers → NRE on cancel chrome and
/// wrong Cancel/Delete visibility for the live instance.
/// </summary>
public sealed class GenerateButtonsStaticEventLeaveContractTests {

	static string Read(params string[] parts) {
		string path = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine(parts));
		Assert.That(File.Exists(path), Is.True, path);
		return File.ReadAllText(path);
	}

	[Test]
	public void Main_UnsubscribesStaticGenerateBusesOnDestroy() {
		string src = Read("Assets", "_gm", "Layouts", "Viewport (MainView)", "GenerateButtons_Main_UI.cs");
		Assert.That(src, Does.Contain("_Act_OnGenerate_started += OnStartedGenerate_cb"));
		Assert.That(src, Does.Contain("protected override void OnDestroy()"));
		Assert.That(src, Does.Contain("_Act_OnGenerate_started -= OnStartedGenerate_cb"));
		Assert.That(src, Does.Contain("_Act_OnGenerate_finished -= OnFinishedGenerate_cb"));
		Assert.That(src, Does.Contain("instance = null"));
		Assert.That(src, Does.Contain("base.OnDestroy()"),
			"theme/dimension leave on the base must still run");
	}

	[Test]
	public void Mini_UnsubscribesStaticGenerateBusesOnDestroy() {
		string src = Read("Assets", "_gm", "Layouts", "Viewport (MainView)", "GenerateButtons_Mini_UI.cs");
		Assert.That(src, Does.Contain("_Act_OnGenerate_started += OnStartedGenerate_cb"));
		Assert.That(src, Does.Contain("protected override void OnDestroy()"));
		Assert.That(src, Does.Contain("_Act_OnGenerate_started -= OnStartedGenerate_cb"));
		Assert.That(src, Does.Contain("_Act_OnGenerate_finished -= OnFinishedGenerate_cb"));
		Assert.That(src, Does.Contain("instance = null"));
		Assert.That(src, Does.Contain("base.OnDestroy()"));
	}
}
