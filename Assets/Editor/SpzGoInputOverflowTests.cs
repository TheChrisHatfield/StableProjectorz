using System.IO;
using NUnit.Framework;

/// <summary>
/// Editable path fields must not use Ellipsis overflow (truncates caret/selection on long paths).
/// </summary>
public sealed class SpzGoInputOverflowTests {

	[Test]
	public void AddTextInput_SourceUsesOverflowNotEllipsisOnEditableText() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		Assert.That(File.Exists(path), Is.True, "AddonUI_MGR.cs must be readable for contract check.");
		string src = File.ReadAllText(path);
		int addText = src.IndexOf("public string AddTextInput(");
		Assert.That(addText, Is.GreaterThanOrEqualTo(0));
		int nextMethod = src.IndexOf("public string AddDropdown(", addText);
		Assert.That(nextMethod, Is.GreaterThan(addText));
		string body = src.Substring(addText, nextMethod - addText);
		Assert.That(body, Does.Contain("TextOverflowModes.Overflow"),
			"Editable InputField text must use Overflow so long exchange paths stay editable.");
		int editableOverflow = body.LastIndexOf("text.overflowMode = TextOverflowModes.Overflow");
		Assert.That(editableOverflow, Is.GreaterThan(0));
	}
}
