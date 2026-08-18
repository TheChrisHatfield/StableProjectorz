using System.IO;
using NUnit.Framework;
using UnityEngine;
using spz;

/// <summary>
/// Add-on Manager search filters the list by folder id and optional displayName.
/// Modern archive had this; the fork FilterBar only had All/Enabled/Disabled pills.
/// </summary>
public class AddonManagerSearchContractTests {

	[Test]
	public void AddonMatchesSearch_MatchesIdAndDisplayName_CaseInsensitive() {
		var info = new Addon_MGR.AddonInfo { displayName = "StableProjectorz GO" };
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, ""), Is.True);
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "   "), Is.True);
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "go"), Is.True,
			"Must match folder id substring.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "projector"), Is.True,
			"Must match displayName substring.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "NOMAD"), Is.False);
		Assert.That(AddonManager_UI.AddonMatchesSearch("CameraTools", null, "camera"), Is.True,
			"Null AddonInfo still matches id.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("CameraTools", null, "lens"), Is.False);
	}

	[Test]
	public void CreatePanel_And_OpenPanel_WireSearchField() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("BuildAddonSearchField"),
			"Panel creation must build the search field.");
		Assert.That(src, Does.Contain("EnsureSearchFieldFromPanel()"),
			"OpenPanel must ensure search on older shells (connectivity).");
		Assert.That(src, Does.Contain("AddonMatchesSearch("),
			"RefreshAddonsList must apply search alongside All/Enabled/Disabled.");
		Assert.That(src, Does.Contain("onValueChanged.AddListener(OnSearchQueryChanged)"),
			"Typing must refresh the list.");
		Assert.That(src, Does.Contain("Search by name or id"),
			"Placeholder must describe name/id scope.");
	}

	[Test]
	public void SearchQuery_NotClearedOnClosePanel() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int closeAt = src.IndexOf("public void ClosePanel()", System.StringComparison.Ordinal);
		Assert.That(closeAt, Is.GreaterThan(0));
		int next = src.IndexOf("public void ", closeAt + 10, System.StringComparison.Ordinal);
		string closeBody = next > closeAt ? src.Substring(closeAt, next - closeAt) : src.Substring(closeAt);
		Assert.That(closeBody, Does.Not.Contain("_searchQuery = \"\""),
			"Close must keep the search text while the manager stays in session.");
		Assert.That(closeBody, Does.Not.Contain("_searchQuery = null"),
			"Close must not wipe the search query.");
	}
}
