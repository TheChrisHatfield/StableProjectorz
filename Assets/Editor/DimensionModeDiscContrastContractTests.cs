using System.IO;
using NUnit.Framework;

/// <summary>
/// Left-column SD/3D/UV/BG discs sat on control_bg ≈ panel charcoal. Unselected must lift toward
/// text_primary; selected stays bright so the strip remains readable under Nomad.
/// </summary>
public sealed class DimensionModeDiscContrastContractTests {

	[Test]
	public void FlatDisc_UnselectedLiftsAndSelectedStaysBright() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Layouts", "Viewport (MainView)", "DimensionMode_MGR.cs");
		Assert.That(File.Exists(path), Is.True, path);
		string src = File.ReadAllText(path);
		int i = src.IndexOf("static void ApplyFlatDisc(", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string body = src.Substring(i, System.Math.Min(900, src.Length - i));
		Assert.That(body, Does.Contain("Color.Lerp(t.textPrimary, t.accent"),
			"selected disc must stay bright against charcoal panel");
		Assert.That(body, Does.Contain("Color.Lerp(t.controlBg, t.textPrimary"),
			"unselected disc must lift off panel_bg — plain control_bg alone still blends");
		Assert.That(body, Does.Not.Contain(": t.controlBg;"),
			"do not paint unselected discs with raw control_bg only");
	}
}
