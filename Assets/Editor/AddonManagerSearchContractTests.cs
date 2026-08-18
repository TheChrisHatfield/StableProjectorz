using System.IO;
using NUnit.Framework;
using UnityEngine;
using spz;

/// <summary>
/// Add-on Manager search is typing-association across all add-on text (not name/id only),
/// and the search bar is a full-width row across the panel.
/// </summary>
public class AddonManagerSearchContractTests {

	[Test]
	public void AddonMatchesSearch_AssociativeAcrossFields_AndTokenizedId() {
		var info = new Addon_MGR.AddonInfo {
			displayName = "StableProjectorz GO",
			description = "Blender bridge for mesh exchange",
			author = "Studio Team",
			listSubtitle = "v1.2 • DCC bridge",
			version = "1.2.0"
		};
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, ""), Is.True);
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "   "), Is.True);
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "go"), Is.True);
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "blender"), Is.True,
			"Must associate with description text.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "studio"), Is.True,
			"Must associate with author.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "dcc"), Is.True,
			"Must associate with subtitle.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("CameraTools", null, "camera tools"), Is.True,
			"CamelCase id must tokenize so multi-word typing associates.");
		Assert.That(AddonManager_UI.AddonMatchesSearch("StableProjectorzGO", info, "blender missing"), Is.False,
			"Every query token must associate (AND).");
		Assert.That(AddonManager_UI.AddonMatchesSearch("CameraTools", null, "lens"), Is.False);
	}

	[Test]
	public void SplitIdentifierTokens_SplitsCamelAndSnake() {
		Assert.That(AddonManager_UI.SplitIdentifierTokens("CameraTools"), Does.Contain("Camera").And.Contain("Tools"));
		Assert.That(AddonManager_UI.SplitIdentifierTokens("ribbon_only_fullscreen"),
			Does.Contain("ribbon").And.Contain("only").And.Contain("fullscreen"));
	}

	[Test]
	public void CreatePanel_And_OpenPanel_WireFullWidthAssociativeSearch() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("BuildAddonSearchField"),
			"Panel creation must build the search field.");
		Assert.That(src, Does.Contain("EnsureSearchFieldFromPanel()"),
			"OpenPanel must ensure search on older shells (connectivity).");
		Assert.That(src, Does.Contain("StretchSearchFieldFullWidth"),
			"Search rectangle must stretch full panel width.");
		Assert.That(src, Does.Contain("BuildAddonSearchHaystack"),
			"Search must build an association haystack, not id/name only.");
		Assert.That(src, Does.Contain("onValueChanged.AddListener(OnSearchQueryChanged)"),
			"Typing must refresh the list.");
		Assert.That(src, Does.Contain("Search add-ons"),
			"Placeholder must be general, not hardcoded to name/id.");
		Assert.That(src, Does.Contain("BuildAddonSearchField(panelObj.transform)"),
			"Search must be a panel sibling (full-bleed), not nested under narrow FilterPills.");
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
