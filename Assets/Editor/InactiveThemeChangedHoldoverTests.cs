using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class InactiveThemeChangedHoldoverTests {
	[Test]
	public void SelectionFrame_UnsubscribesOnDestroyNotDisable_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "Icons", "IconUI", "IconUI_SelectionFrame.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Not.Contain("void OnDisable()"));
		Assert.That(src, Does.Contain("void OnDestroy()"));
		Assert.That(src, Does.Contain("ThemeChanged -= ApplyThemeTokens"));
	}

	[Test]
	public void OwnershipHub_KeepsThemeChangedUntilDestroy_Source() {
		string path = Path.Combine(Application.dataPath, "_gm", "Features", "AddonSystem", "SpzUiThemeRoleMatrix.cs");
		string src = File.ReadAllText(path);
		int hub = src.IndexOf("sealed class SpzUiThemeOwnershipHub", System.StringComparison.Ordinal);
		Assert.That(hub, Is.GreaterThan(0));
		string body = src.Substring(hub, System.Math.Min(1200, src.Length - hub));
		Assert.That(body, Does.Not.Contain("void OnDisable()"));
		Assert.That(body, Does.Contain("void OnDestroy()"));
		Assert.That(body, Does.Contain("ThemeChanged -= OnThemeChanged"));
	}
}
