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

	[Test]
	public void PerspectiveCenterVoronoi_PicksNearestActiveColumn() {
		var centers = new UnityEngine.Vector2[] {
			new UnityEngine.Vector2(0.2f, 0.5f),
			new UnityEngine.Vector2(0.5f, 0.5f),
			new UnityEngine.Vector2(0.8f, 0.5f),
		};
		var active = new[] { true, true, true };
		Assert.That(MultiviewPinLayoutRules.FindNearestPerspectiveCenterIndex(
			new UnityEngine.Vector2(0.75f, 0.5f), centers, active), Is.EqualTo(2));
		Assert.That(MultiviewPinLayoutRules.FindNearestPerspectiveCenterIndex(
			new UnityEngine.Vector2(0.3f, 0.5f), centers, active), Is.EqualTo(0));
		active[2] = false;
		Assert.That(MultiviewPinLayoutRules.FindNearestPerspectiveCenterIndex(
			new UnityEngine.Vector2(0.9f, 0.5f), centers, active), Is.EqualTo(1));
	}

	[Test]
	public void NavLockClearRequiresSameOwner() {
		// Documents the owner-scoped sticky-lock contract used by UserCameras_MGR:
		// a Clear from Orbit/Dolly must not wipe a Move/Pan lock held by a different owner.
		object move = new object();
		object orbit = new object();
		Assert.That(MultiviewPinLayoutRules.NavLockClearShouldApply(currentOwner: move, clearRequester: orbit), Is.False);
		Assert.That(MultiviewPinLayoutRules.NavLockClearShouldApply(currentOwner: move, clearRequester: move), Is.True);
		Assert.That(MultiviewPinLayoutRules.NavLockClearShouldApply(currentOwner: null, clearRequester: move), Is.False);
	}

	[Test]
	public void MmbOnMeshPrefersPanOverPinGrab() {
		Assert.That(MultiviewPinLayoutRules.MmbShouldPreferPanOverPinGrab(cursorOverMesh: true), Is.True);
		Assert.That(MultiviewPinLayoutRules.MmbShouldPreferPanOverPinGrab(cursorOverMesh: false), Is.False);
	}

	[Test]
	public void MmbPanDoesNotLiveUpdatePerspectiveCenter() {
		// Documents CameraPanning: translate-only during drag (standard/single-asset feel).
		Assert.That(MultiviewPinLayoutRules.MmbPanShouldUpdatePerspectiveCenterEveryFrame(), Is.False);
	}
}
