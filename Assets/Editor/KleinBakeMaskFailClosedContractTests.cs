using System;
using System.IO;
using NUnit.Framework;

/// <summary>
/// When Klein inpaint-mode txt2img requires a bake mask and capture fails, generation must abort
/// (reuse structure-fail gate) — not continue with a missing mask that bake treats as full plate.
/// </summary>
public sealed class KleinBakeMaskFailClosedContractTests {

	[Test]
	public void MissingRequiredBakeMask_SetsStructureAttachFailed() {
		string path = Path.Combine(Directory.GetCurrentDirectory(),
			"Assets", "_gm", "Features", "StableDiffusion", "Input Panel", "SD_Generate_PayloadMaker.cs");
		string src = File.ReadAllText(path);
		int i = src.IndexOf("CaptureKleinTxt2imgInpaintBakeMask(intermediates_)", StringComparison.Ordinal);
		Assert.That(i, Is.GreaterThan(0));
		string after = src.Substring(i, Math.Min(700, src.Length - i));
		Assert.That(after, Does.Contain("!intermediates_.kleinTxt2imgInpaintBakeMask"));
		Assert.That(after, Does.Contain("kleinStructureAttachFailed = true"),
			"failed required bake-mask capture must trip the gen abort flag");
		Assert.That(after, Does.Contain("inpaint bake mask unavailable"));
	}
}
