using NUnit.Framework;
using spz;

public sealed class SdDisconnectPlaceholderTests {

	[Test]
	public void IsPlaceholder_RecognizesLegacyCopy() {
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(
			"Not Connected yet.\nCheck Black Window"), Is.True);
	}

	[Test]
	public void IsPlaceholder_RecognizesCurrentCopy() {
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(
			SdDisconnectPlaceholder.DisplayText), Is.True);
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(
			"Diffusion Neural Network\nNot yet connected."), Is.True);
	}

	[Test]
	public void IsPlaceholder_RejectsRealModelNames() {
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(null), Is.False);
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(""), Is.False);
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder("None"), Is.False);
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder("flux1-schnell-fp8.safetensors"), Is.False);
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder("Automatic"), Is.False);
	}

	[Test]
	public void DisplayText_IsSdSpecificNotGenericGen3DCopy() {
		// Gen3D Dropdown Holder must keep neutral legacy wording; SD panels use DisplayText.
		Assert.That(SdDisconnectPlaceholder.DisplayText, Does.Contain("Diffusion Neural Network"));
		Assert.That(SdDisconnectPlaceholder.DisplayText, Does.Not.Contain("Check Black Window"));
	}

	[Test]
	public void IsPlaceholder_MeansSelectedNameShouldBeEmpty() {
		// Mirrors GetSelectedModel_name / GetSelectedVAE_name: placeholder must not be treated as a checkpoint/VAE id.
		string chosen = SdDisconnectPlaceholder.DisplayText;
		Assert.That(SdDisconnectPlaceholder.IsPlaceholder(chosen), Is.True);
		string selectedName = SdDisconnectPlaceholder.IsPlaceholder(chosen) ? "" : chosen;
		Assert.That(selectedName, Is.EqualTo(""));
	}
}
