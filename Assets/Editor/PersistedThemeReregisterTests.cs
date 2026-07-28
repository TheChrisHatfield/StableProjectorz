using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class PersistedThemeReregisterTests {

	[Test]
	public void TryRestorePersistedTheme_SourceReregistersAnyThemeId() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("public static bool TryRestorePersistedTheme", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		int next = src.IndexOf("\n\t\tpublic static ", idx + 20, System.StringComparison.Ordinal);
		if (next < 0) next = src.Length;
		string body = src.Substring(idx, next - idx);
		Assert.That(body, Does.Contain("TryRegisterTheme(themeId, display, tokens, owner"));
		Assert.That(body, Does.Not.Contain(
			"if (string.Equals(themeId, \"nomad-inspired\", StringComparison.Ordinal))\r\n\t\t\t\t\tTryRegisterTheme"));
		Assert.That(body, Does.Not.Contain(
			"if (string.Equals(themeId, \"nomad-inspired\", StringComparison.Ordinal))\n\t\t\t\t\tTryRegisterTheme"));
	}
}
