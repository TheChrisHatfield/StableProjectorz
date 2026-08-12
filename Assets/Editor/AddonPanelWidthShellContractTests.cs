using System.IO;
using NUnit.Framework;

public sealed class AddonPanelWidthShellContractTests {

	[Test]
	public void CreatePanel_AppliesPanelWidthToShellNotButtons() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SpzUiThemeOps.ApplyPanelWidth(shellLe)"),
			"CreatePanel must put panel_width on the AddonPanel shell LayoutElement.");
		int addBtn = src.IndexOf("public string AddButton(", System.StringComparison.Ordinal);
		int addTog = src.IndexOf("public string AddToggle(", addBtn, System.StringComparison.Ordinal);
		string btnBody = src.Substring(addBtn, addTog - addBtn);
		Assert.That(btnBody, Does.Not.Contain("Active.panelWidth"),
			"AddButton must not use panel_width as preferredWidth (Nomad 400px row blow-up).");
		Assert.That(btnBody, Does.Contain("const float btnW = 280f"));
	}
}
