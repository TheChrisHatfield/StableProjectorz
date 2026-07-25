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

		/// <summary>
		/// Which active camera "owns" the cursor: nearest perspective-center in the same [0,1] space
		/// the pins live in (inner-viewport / projectionMat_center). Prefer this over raw pin
		/// GameObject screen positions when digits have drifted — Voronoi of the centers matches
		/// the visual multi-view columns.
		/// </summary>
		/// <param name="cursor01">Cursor in the same space as <paramref name="centers01"/>.</param>
		/// <param name="centers01">Perspective center per camera index (may include inactive slots).</param>
		/// <param name="active">Parallel flags; inactive slots are skipped.</param>
		/// <returns>Owning camera index, or -1 if none active.</returns>
		public static int FindNearestPerspectiveCenterIndex(
			UnityEngine.Vector2 cursor01,
			UnityEngine.Vector2[] centers01,
			bool[] active) {
			if (centers01 == null || active == null) { return -1; }
			int n = UnityEngine.Mathf.Min(centers01.Length, active.Length);
			int best = -1;
			float bestSqr = float.MaxValue;
			for (int i = 0; i < n; ++i) {
				if (!active[i]) { continue; }
				float dx = centers01[i].x - cursor01.x;
				float dy = centers01[i].y - cursor01.y;
				float sqr = dx * dx + dy * dy;
				if (sqr >= bestSqr) { continue; }
				bestSqr = sqr;
				best = i;
			}
			return best;
		}

		/// <summary>Owner-scoped sticky nav lock: only the locker may clear.</summary>
		public static bool NavLockClearShouldApply(object currentOwner, object clearRequester) {
			if (clearRequester == null || currentOwner == null) { return false; }
			return ReferenceEquals(currentOwner, clearRequester);
		}

		/// <summary>
		/// MMB on a mesh under the owning column should pan (single-asset feel), not steal the
		/// POV digit even when the cursor is inside the pin grab radius. Pin drag only when the
		/// cursor is near the pin and NOT over a mesh.
		/// </summary>
		public static bool MmbShouldPreferPanOverPinGrab(bool cursorOverMesh) => cursorOverMesh;

		/// <summary>
		/// Live perspective-center / projection updates during MMB pan compound with camera Translate
		/// and break standard-mode cursor tracking. Track pin *UI* every frame; never auto-commit
		/// projection on release (that "snap" yanked multi-view framing after positioning).
		/// </summary>
		public static bool MmbPanShouldUpdatePerspectiveCenterEveryFrame() => false;

		/// <summary>POV digit UI should follow the panned asset every frame (without frustum changes).</summary>
		public static bool MmbPanShouldTrackPinUiEveryFrame() => true;

		/// <summary>
		/// Committing perspective-center onto the mesh when MMB is released fights free multi-view
		/// framing. Pin drag is the intentional way to move projection centers.
		/// </summary>
		public static bool MmbPanShouldCommitPerspectiveCenterOnRelease() => false;

		/// <summary>
		/// Multi-view click/hover must not cast through other columns' cameras: the same viewport UV
		/// with a different pin shift hits a neighboring asset when figures sit close on screen.
		/// </summary>
		public static bool MeshPickMayUseOtherViewCameras(bool isMultiView) => !isMultiView;

		/// <summary>
		/// Edge ID fallback should pick the nearest non-zero texel (true nearest neighbor), not an
		/// area-weighted vote — larger neighbors won when assets were close together.
		/// </summary>
		public static bool MeshPickIdEdgeFallbackUsesNearestNeighbor() => true;

		/// <summary>
		/// On mesh select, POV digits lock to that mesh's bounds center and keep the assignment when
		/// more meshes are multi-selected (per-column), instead of jumping with hover focus.
		/// </summary>
		public static bool PovDigitLocksToSelectedMeshCenter() => true;

		/// <summary>Sole selection: lock every active multi-view column to that one mesh.</summary>
		public static bool SoleSelectionLocksAllActivePins() => true;
	}
}
