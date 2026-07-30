using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// spz.ui.set_value must preserve bool/int/float — coercing int→float breaks dropdowns; skipping bool breaks toggles.
/// </summary>
public sealed class AddonSocketSetValueTypeTests {

	[Test]
	public void SetValue_SourcePreservesBoolIntAndFloat() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"..",
			"Assets/_gm/Features/AddonSystem/Addon_SocketServer.cs"));
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int setValue = src.IndexOf("case \"spz.ui.set_value\":", System.StringComparison.Ordinal);
		Assert.That(setValue, Is.GreaterThan(0));
		int nextCase = src.IndexOf("default:", setValue, System.StringComparison.Ordinal);
		Assert.That(nextCase, Is.GreaterThan(setValue));
		string body = src.Substring(setValue, nextCase - setValue);
		Assert.That(body, Does.Contain("JTokenType.Boolean"), "Boolean set_value must map to bool for toggles.");
		Assert.That(body, Does.Contain("JTokenType.Integer"), "Integer set_value must map to int for dropdowns.");
		Assert.That(body, Does.Contain("JTokenType.Float"), "Float set_value must map to float for sliders.");
		Assert.That(body, Does.Not.Contain("JTokenType.Float || valueToken.Type == JTokenType.Integer"),
			"Must not coerce Integer and Float through the same ToObject<float>() branch.");
	}
}
