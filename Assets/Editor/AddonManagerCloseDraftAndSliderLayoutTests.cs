using System.IO;
using NUnit.Framework;

public sealed class AddonManagerCloseDraftAndSliderLayoutTests {

	[Test]
	public void ClosePanel_WarnsWhenDraftDirty() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonManager_UI.cs");
		string src = File.ReadAllText(path);
		int method = src.IndexOf("public void ClosePanel()", System.StringComparison.Ordinal);
		int next = src.IndexOf("void OnLoadAddonsNow()", method, System.StringComparison.Ordinal);
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Contain("_draftDirty"));
		Assert.That(body, Does.Contain("Closed without Save settings"));
		Assert.That(src, Does.Contain("Remember on — next launch will restore"));
	}

	[Test]
	public void AddSliderAndDropdown_HaveLayoutElementRows() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int slider = src.IndexOf("public string AddSlider(", System.StringComparison.Ordinal);
		int textIn = src.IndexOf("public string AddTextInput(", slider, System.StringComparison.Ordinal);
		string sliderBody = src.Substring(slider, textIn - slider);
		Assert.That(sliderBody, Does.Contain("sliderLe.preferredHeight = 48f"));
		int drop = src.IndexOf("public string AddDropdown(", System.StringComparison.Ordinal);
		string dropBody = src.Substring(drop, 1200);
		Assert.That(dropBody, Does.Contain("dropdownLe.preferredHeight = 48f"));
		Assert.That(dropBody, Does.Contain("overflowMode = TextOverflowModes.Ellipsis"));
	}
}
