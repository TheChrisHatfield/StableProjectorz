using UnityEngine;

namespace spz {

	/// <summary>
	/// Camera side of the viewport orientation gizmo: read the current view rotation for the axis discs,
	/// and fly the resolved <see cref="View_UserCamera"/> (nav-lock → current → first active) to a world axis
	/// (front / back / left / right / top / bottom) or back to an overview of the whole scene.
	/// Snapping reuses <see cref="CameraFocus.Restore_CameraPlacement"/> so the move is the same animated
	/// "selfie stick" lerp as camera-icon restore, instead of teleporting the transform.
	/// </summary>
	public static class ViewportAxisGizmo_CameraOps {

		/// <summary>
		/// Camera the gizmo reflects and drives.
		/// A corner-docked chrome control must NOT use <see cref="UserCameras_MGR.NearestToCursor"/>: the cursor is
		/// over the top-right of the composite when the user clicks the gizmo, so Voronoi / pin distance always
		/// resolves to the rightmost multiview column. Prefer the sticky nav lock (mid-orbit/pan), then the
		/// marked-current camera, then the first active view.
		/// </summary>
		public static View_UserCamera ResolveGizmoCamera() {
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) {
				return null;
			}
			// Mid-gesture: orbit/pan/move lock owns the column even if _curr has not caught up yet.
			var locked = mgr.TryGetNavigationLockedCamera();
			if (locked != null) {
				return locked;
			}
			var cam = mgr._curr_viewCamera;
			if (cam != null && cam.gameObject.activeInHierarchy) {
				return cam;
			}
			for (int i = 0; i < mgr.GetViewCameraCount(); i++) {
				var c = mgr.GetViewCamera(i);
				if (c != null && c.gameObject.activeInHierarchy) {
					return c;
				}
			}
			return cam;
		}

		/// <summary>World rotation of the camera the balls orbit around; identity when no camera rig is loaded yet.</summary>
		public static Quaternion CurrentViewRotation() {
			var cam = ResolveGizmoCamera();
			return cam != null ? cam.transform.rotation : Quaternion.identity;
		}

		/// <summary>
		/// Orbit target for axis snaps. Prefer the current selection (same pivot orbit / F use); when nothing is
		/// selected, fall back to every loaded mesh so a deselect does not silently snap around world origin.
		/// </summary>
		public static Vector3 ResolvePivot() {
			var models = ModelsHandler_3D.instance;
			if (models == null) {
				return Vector3.zero;
			}
			var selected = models.selectedMeshes;
			if (selected != null && selected.Count > 0) {
				return models.GetTotalBounds_ofSelectedMeshes().center;
			}
			var all = models.meshes;
			if (all != null && all.Count > 0) {
				return models.GetTotalBounds_ofAllMeshes().center;
			}
			return Vector3.zero;
		}

		public static bool IsGizmoUsable() {
			if (ResolveGizmoCamera() == null) {
				return false;
			}
			var dim = DimensionMode_MGR.instance;
			return dim == null || dim.is_3d_navigation_allowed;
		}

		/// <summary>
		/// Fly the current view camera onto <paramref name="worldAxis"/> (direction from pivot towards the camera),
		/// keeping the current distance and FOV. False when no camera / 3D navigation is off.
		/// </summary>
		public static bool TrySnapToAxis(Vector3 worldAxis) {
			if (worldAxis.sqrMagnitude < 1e-6f) {
				return false;
			}
			if (!IsGizmoUsable()) {
				return false;
			}
			var cam = ResolveGizmoCamera();
			var focus = cam != null ? cam.cameraFocus : null;
			if (focus == null) {
				return false;
			}
			Vector3 pivot = ResolvePivot();
			float distance = ViewportAxisGizmo_Math.SnapDistance(cam.transform.position, pivot);
			Vector3 destPos = ViewportAxisGizmo_Math.CameraPositionForAxis(pivot, worldAxis, distance);
			Quaternion destRot = ViewportAxisGizmo_Math.CameraRotationForAxis(worldAxis);
			var pov = new CameraPovInfo(true, destPos, destRot, ResolveFov(cam), cam._projectionMat_center);
			// Clicking a second axis mid-flight must retarget, not be dropped on the floor.
			focus.Restore_CameraPlacement(pov, pivot, interruptCurrentFly: true);
			return true;
		}

		/// <summary>
		/// FOV to record in the snap POV. <see cref="ViewCamera_FOV._trueCameraFov"/> stays -1 until something sets
		/// it, so fall back to the live camera instead of writing a nonsense FOV into the POV info.
		/// </summary>
		public static float ResolveFov(View_UserCamera cam) {
			if (cam == null) {
				return 60f;
			}
			float tracked = cam.fovMgr != null ? cam.fovMgr._trueCameraFov : -1f;
			if (tracked >= 1f && tracked <= 179f) {
				return tracked;
			}
			return cam.myCamera != null ? cam.myCamera.fieldOfView : 60f;
		}

		/// <summary>
		/// Lantern center button: frame the whole scene (every loaded mesh), not just the current selection — the
		/// gizmo lantern is an "overview / frame all" control. Flies the current camera the same animated way as F.
		/// </summary>
		public static bool TryOverview() {
			if (!IsGizmoUsable()) {
				return false;
			}
			var cam = ResolveGizmoCamera();
			var focus = cam != null ? cam.cameraFocus : null;
			if (focus == null) {
				return false;
			}
			// Frame_Bounds_maybe returns false when the fly cannot start (missing camera / coroutine host).
			if (!HasSomethingToFrame()) {
				Viewport_StatusText.instance?.ShowStatusText(
					"Nothing loaded to frame — import a mesh first.", false, 2f, false);
				return false;
			}
			return focus.Frame_Bounds_maybe(ResolveSceneBounds(), forceTheFocus: true);
		}

		/// <summary>World-space bounds of the whole scene (all meshes) the lantern frames.</summary>
		public static Bounds ResolveSceneBounds() {
			var models = ModelsHandler_3D.instance;
			return models != null ? models.GetTotalBounds_ofAllMeshes() : new Bounds();
		}

		/// <summary>Overview needs at least one loaded mesh — the lantern frames the entire scene, selected or not.</summary>
		public static bool HasSomethingToFrame() {
			var models = ModelsHandler_3D.instance;
			if (models == null) {
				return false;
			}
			var all = models.meshes;
			return all != null && all.Count > 0;
		}
	}
}
