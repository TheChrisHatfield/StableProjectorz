using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Leave Nomad ownership roots: Krita Content parent, addon transparent-face labels, icon-only snapshot.
/// </summary>
public sealed class BoundChromePass49LeaveOwnershipTests {

	[Test]
	public void KritaRestoreSectionShell_SourceRestoresParentContent() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/Paint/PaintTab/PaintTab_KritaLayout_UI.cs"));
		string src = File.ReadAllText(path);
		int idx = src.IndexOf("static void RestoreSectionShell", System.StringComparison.Ordinal);
		Assert.That(idx, Is.GreaterThan(0));
		string body = src.Substring(idx, System.Math.Min(600, src.Length - idx));
		Assert.That(body, Does.Contain("section.parent.name == \"Content\""));
		Assert.That(body, Does.Contain("RestoreBoundChromeUnder(section.parent)"));
	}

	[Test]
	public void ApplyToAddonUiRoot_SourceSkipsLabelClearOnTransparentHitPads() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/SpzUiThemeOps.cs"));
		string src = File.ReadAllText(path);
		int fn = src.IndexOf("public static void ApplyToAddonUiRoot", System.StringComparison.Ordinal);
		Assert.That(fn, Is.GreaterThan(0));
		string body = src.Substring(fn, System.Math.Min(4500, src.Length - fn));
		Assert.That(body, Does.Contain("targetGraphic.color.a < 0.08f"));
		Assert.That(body, Does.Contain("Transparent Selectable hit pads"));
	}

	[Test]
	public void AddonManager_SourceSnapshotsIconOnlyLabelColor() {
		string path = Path.GetFullPath(Path.Combine(
			Application.dataPath,
			"_gm/Features/AddonSystem/AddonManager_UI.cs"));
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("SnapshotAuthoredGraphicForTheme(label)"));
		int restore = src.IndexOf("static void RestoreHeaderButtonAuthoredChrome", System.StringComparison.Ordinal);
		Assert.That(restore, Is.GreaterThan(0));
		string body = src.Substring(restore, System.Math.Min(500, src.Length - restore));
		Assert.That(body, Does.Contain("RestoreAuthoredGraphic(label)"));
	}
}
