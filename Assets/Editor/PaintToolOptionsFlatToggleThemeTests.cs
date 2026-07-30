using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Paint Tool Options radios use bevel Checkmark faces — ThemeCheckboxToggle would treat them as real ✓.
/// PreferFlatToolToggles routes them through ThemeFlatToolToggle under Nomad.
/// </summary>
public sealed class PaintToolOptionsFlatToggleThemeTests {

	[Test]
	public void PaintCollect_ToolOptionsUsesPreferFlatToolToggles() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Paint", "PaintTab", "PaintTab_CollectPaintUI.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("ThemeOwnedSection(_layout.ToolOptionsSection, t, preferFlatToolToggles: true)"));
		Assert.That(src, Does.Contain("ThemeFlatToolToggle(toggle, fill, t.accent, t.textPrimary)"));
	}
}
