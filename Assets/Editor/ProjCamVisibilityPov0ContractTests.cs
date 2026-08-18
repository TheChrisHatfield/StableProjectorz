using System.IO;
using NUnit.Framework;

/// <summary>
/// Multi-POV visibility init must size helpers from the first *enabled* POV mask.
/// Index 0 is null when that camera is disabled, and GenData_Masks intentionally stores nulls there.
/// </summary>
public sealed class ProjCamVisibilityPov0ContractTests {

	[Test]
	public void VisibilityInit_DoesNotAssumePov0MaskExists() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Camera", "Projections", "ProjCam_HelpTextures_Init.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("public void Make_Visibilities_and_Alignments", System.StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		int j = src.IndexOf("void ImproveVisibility_byAlignments", i, System.StringComparison.Ordinal);
		string body = src.Substring(i, j - i);
		Assert.That(body, Does.Not.Contain("onWillRenderPov(0)"),
			"POV0 may be disabled — must not NRE on a null mask slot");
		Assert.That(body, Does.Contain("FindFirstEnabledPovUdims"),
			"size must come from the first enabled POV that actually has a mask");
		Assert.That(body, Does.Contain("_byproductsOfRequest != null"),
			"byproducts container can be null; only the texture was optional before");
	}
}
