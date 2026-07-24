namespace spz {

	/// <summary>
	/// Shared multi-view pin layout rules (kept pure for editor tests).
	/// </summary>
	public static class MultiviewPinLayoutRules {

		/// <summary>
		/// True when the multi-view slider (or equivalent) changes to a multi-camera count that
		/// needs default pin centers re-applied so POV digits sit in the correct viewport columns.
		/// </summary>
		public static bool ShouldAutoLayoutPinsAfterCamCountChange(int previousWantedCams, int wantedCams) {
			if (wantedCams <= 1) { return false; }
			return previousWantedCams != wantedCams;
		}

		/// <summary>UI digit for a 0-based view-camera index (pin 0 shows "1").</summary>
		public static int PinLabelForCameraIndex(int cameraIndex) => cameraIndex + 1;

		/// <summary>
		/// Init must not mark every view-camera slot enabled just to pick a placement variant —
		/// that seeds inactive cameras with multi-pin leftover centers.
		/// </summary>
		public static bool ShouldSeedAllCamerasAsEnabledDuringInit() => false;
	}
}
