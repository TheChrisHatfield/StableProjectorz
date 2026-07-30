using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Leave Nomad must restore Gen3D web-find + prompt presets when remapped outside the host root.
/// </summary>
public sealed class Gen3dPromptLeaveWebFindThemeTests {

	[Test]
	public void Gen3dPrompt_LeaveRestoresWebFindAndPresets() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "3D Generate", "Generation3D_Prompt_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreWebFindChrome()"));
		Assert.That(src, Does.Contain("RestorePresetChrome()"));
	}
}

/// <summary>
/// Leave Nomad must restore SD web-find / prompt presets outside the movable root (litmus vs resolution chips).
/// </summary>
public sealed class SdInputPanelLeaveWebFindThemeTests {

	[Test]
	public void SdInputPanel_LeaveRestoresWebFindAndPromptPresets() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "StableDiffusion", "Input Panel", "SD_InputPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("RestoreWebFindAndPromptPresets(rootRestore)"));
		Assert.That(src, Does.Contain("IsWebFindButton(btn)"));
		Assert.That(src, Does.Contain("IsPromptPresetToggle(toggle)"));
	}
}
