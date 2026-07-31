using System.IO;
using NUnit.Framework;

/// <summary>
/// Trellis/Gen3D connection ping must not require StableDiffusion_Hub — that left 3D SERV red forever.
/// </summary>
public sealed class ConnectionPanelTrellisPingContractTests {

	[Test]
	public void CheckConnection_TrellisDoesNotRequireSdHub() {
		string path = Path.Combine(
			Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Connection", "ConnectionPanel_UI.cs");
		Assert.That(File.Exists(path), Is.True);
		string src = File.ReadAllText(path);
		int method = src.IndexOf("IEnumerator CheckConnection(", System.StringComparison.Ordinal);
		Assert.That(method, Is.GreaterThan(0));
		int next = src.IndexOf("string where_to_ping(", method, System.StringComparison.Ordinal);
		Assert.That(next, Is.GreaterThan(method));
		string body = src.Substring(method, next - method);
		Assert.That(body, Does.Not.Contain("url_for_ping==\"\"  ||  StableDiffusion_Hub.instance==null"),
			"Must not skip all panels when SD hub is null.");
		Assert.That(body, Does.Contain("ConnectionPanel_Kind.Trellis")
			.Or.Contain("ConnectionPanel_Kind.StableDiffusion"),
			"Hub gating must be SD-panel scoped.");
		Assert.That(body, Does.Contain("sdNeedsHub"),
			"SD-only hub wait must be explicit so Trellis keeps pinging.");
	}
}
