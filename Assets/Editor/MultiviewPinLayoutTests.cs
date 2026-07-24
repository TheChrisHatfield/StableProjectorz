using NUnit.Framework;
using spz;

/// <summary>
/// Pure rules for multi-view POV pin layout / hover routing.
/// Runtime UI is covered by wiring these helpers into MultiView_Ribbon_UI / CameraPanning.
/// </summary>
public sealed class MultiviewPinLayoutTests {

	[Test]
	public void ShouldAutoLayoutPins_WhenEnteringOrChangingMultiView() {
		Assert.That(MultiviewPinLayoutRules.ShouldAutoLayoutPinsAfterCamCountChange(1, 3), Is.True);
		Assert.That(MultiviewPinLayoutRules.ShouldAutoLayoutPinsAfterCamCountChange(2, 4), Is.True);
		Assert.That(MultiviewPinLayoutRules.ShouldAutoLayoutPinsAfterCamCountChange(3, 3), Is.False);
		Assert.That(MultiviewPinLayoutRules.ShouldAutoLayoutPinsAfterCamCountChange(3, 1), Is.False);
		Assert.That(MultiviewPinLayoutRules.ShouldAutoLayoutPinsAfterCamCountChange(0, 1), Is.False);
	}

	[Test]
	public void PinLabelIsOneBasedCameraIndex() {
		Assert.That(MultiviewPinLayoutRules.PinLabelForCameraIndex(0), Is.EqualTo(1));
		Assert.That(MultiviewPinLayoutRules.PinLabelForCameraIndex(2), Is.EqualTo(3));
	}

	[Test]
	public void InitPinsMustNotPretendAllCamerasAreEnabledForLayout() {
		// Guardrail for CamerasMGR_POVdefaults_UI.InitPins_To_DefaultLocations:
		// building a pov list with every slot wasEnabled=true selects the 6-pin variant and
		// writes leftover centers onto inactive cameras. Only camera 0 should be centered at startup.
		Assert.That(MultiviewPinLayoutRules.ShouldSeedAllCamerasAsEnabledDuringInit(), Is.False);
	}
}
