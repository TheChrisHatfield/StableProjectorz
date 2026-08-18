using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// Copy-install must only run the ZBrush or Painter installer for that host id — an unknown id
/// must fail closed, not silently install Painter.
/// </summary>
public sealed class SpzGoCopyInstallHostGateContractTests {

	[Test]
	public void NativeCopyInstall_FailsClosedForUnknownHostId() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "AddonSystem", "AddonUI_MGR.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("void SpzGoNativeInstallCopyBridge(", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int end = src.IndexOf("void SpzGoRefreshHostReadiness(", i, StringComparison.Ordinal);
		string body = src.Substring(i, end - i);
		Assert.That(body, Does.Contain("SpzGoHosts.ZBrushId"));
		Assert.That(body, Does.Contain("SpzGoHosts.PainterId"));
		Assert.That(body, Does.Contain("no copy-install path for host"),
			"unknown hostId must not fall through to TryInstallSpzGoPainterBridge");
		Assert.That(body, Does.Contain("TryInstallSpzGoPainterBridge"));
		// Ternary fall-through to Painter is the bug — require an else-if / else fail path.
		Assert.That(body, Does.Not.Contain("? fp.TryInstallSpzGoZBrushBridge(out message)\r\n\t\t\t\t: fp.TryInstallSpzGoPainterBridge")
			.And.Not.Contain("? fp.TryInstallSpzGoZBrushBridge(out message)\n\t\t\t\t: fp.TryInstallSpzGoPainterBridge"));
	}
}
