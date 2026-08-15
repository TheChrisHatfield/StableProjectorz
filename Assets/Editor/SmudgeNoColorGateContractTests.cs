using System.IO;
using NUnit.Framework;

/// <summary>
/// No-Color smudge must stay on ActiveLayer.NoColorMask, not fall through to mesh accumulation.
/// </summary>
public sealed class SmudgeNoColorGateContractTests {

	[Test]
	public void LayerSmudgeGate_AcceptsActiveNoColorMask() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Inpaint", "SmudgeStrokeRouter.cs");
		string src = File.ReadAllText(path);
		int gateAt = src.IndexOf("public static bool LayerSmudgeGateOpen(", System.StringComparison.Ordinal);
		Assert.That(gateAt, Is.GreaterThan(0));
		string gateBody = src.Substring(gateAt, System.Math.Min(700, src.Length - gateAt));
		Assert.That(gateBody, Does.Contain("active.NoColorMask"),
			"Gate must open for NoColorMask or SameShape(mesh) steals the stroke.");
		Assert.That(gateBody, Does.Contain("active.Content"));
	}

	[Test]
	public void LayerGatePlan_UsesNoColorUndoKind() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "Paint", "Inpaint", "SmudgeStrokeRouter.cs");
		string src = File.ReadAllText(path);
		Assert.That(src, Does.Contain("PaintUndoNonStackTarget.InpaintNoColorMask"),
			"Smudging NoColorMask must schedule NoColor undo, not Content/mesh.");
		int layerGatePlan = src.IndexOf("plan.Domain = WriteDomain.LayerStack;", System.StringComparison.Ordinal);
		Assert.That(layerGatePlan, Is.GreaterThan(0));
		string before = src.Substring(System.Math.Max(0, layerGatePlan - 350), 350);
		Assert.That(before, Does.Contain("InpaintNoColorMask"));
	}
}
