using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class AddonManagerPrefsUninstallPlacementTests {
	[Test]
	public void Uninstall_LivesUnderPreferencesCard_NotHeaderRow_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int create = src.IndexOf("void CreateAddonListItem(", System.StringComparison.Ordinal);
		Assert.That(create, Is.GreaterThan(0));
		int prefsCard = src.IndexOf("var prefsCard = new GameObject(\"PreferencesCard\")", create, System.StringComparison.Ordinal);
		int removeCreate = src.IndexOf("var removeBtnObj = new GameObject(\"RemoveButton\")", create, System.StringComparison.Ordinal);
		Assert.That(prefsCard, Is.GreaterThan(create));
		Assert.That(removeCreate, Is.GreaterThan(prefsCard),
			"Uninstall must be created after PreferencesCard so it can parent under prefs.");
		string removeWindow = src.Substring(removeCreate, System.Math.Min(350, src.Length - removeCreate));
		Assert.That(removeWindow, Does.Contain("prefsCard.transform"),
			"RemoveButton must parent under PreferencesCard, not HeaderRow.");
		Assert.That(src, Does.Contain("PreferencesBody/PreferencesCard"),
			"Theme path must find Uninstall under the prefs card.");
	}

	[Test]
	public void ShowInRibbon_UsesRadioDialNotGreenPlate_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("CircleRing"),
			"Host pref must use a radio dial ring.");
		Assert.That(src, Does.Contain("Show in Command Ribbon"));
		Assert.That(src, Does.Not.Contain("In Command Ribbon ✓"),
			"Giant green labeled plate copy must stay removed.");
		Assert.That(src, Does.Not.Contain("LockShowInRibbonButtonLayout"),
			"Wide green action-button layout lock must stay removed.");
		int theme = src.IndexOf("static void ThemeShowInRibbonDial(", System.StringComparison.Ordinal);
		string body = src.Substring(theme, System.Math.Min(900, src.Length - theme));
		Assert.That(body, Does.Contain("hit.color = Color.clear"),
			"Dial hit target must stay clear (no solid green plate).");
		Assert.That(body, Does.Contain("CircleRing"));
	}
}
