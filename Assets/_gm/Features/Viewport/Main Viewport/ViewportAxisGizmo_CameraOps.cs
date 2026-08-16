using UnityEngine;

namespace spz {

	/// <summary>
	/// Camera side of the viewport orientation gizmo: read the current view rotation for the axis balls,
	/// and fly the current <see cref="View_UserCamera"/> to a world axis (front / back / left / right / top / bottom)
	/// or back to an overview of the selection.
	/// Snapping reuses <see cref="CameraFocus.Restore_CameraPlacement"/> so the move is the same animated
	/// "selfie stick" lerp as camera-icon restore, instead of teleporting the transform.
	/// </summary>
	public static class ViewportAxisGizmo_CameraOps {

		/// <summary>Camera the gizmo reflects and drives (the orbit-priority one).</summary>
		public static View_UserCamera ResolveGizmoCamera() {
			var mgr = UserCameras_MGR.instance;
			if (mgr == null) {
				return null;
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

		/// <summary>Center of the current selection (orbit target). Origin when nothing is loaded / selected.</summary>
		public static Vector3 ResolvePivot() {
			var models = ModelsHandler_3D.instance;
			if (models == null) {
				return Vector3.zero;
			}
			return models.GetTotalBounds_ofSelectedMeshes().center;
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

		/// <summary>Lantern center button: frame the selection again (same path as pressing F over the viewport).</summary>
		public static bool TryOverview() {
			if (!IsGizmoUsable()) {
				return false;
			}
			var cam = ResolveGizmoCamera();
			var focus = cam != null ? cam.cameraFocus : null;
			if (focus == null) {
				return false;
			}
			// Focus_Selection_maybe quietly returns when nothing is selected, so check first instead of reporting
			// success for a button press that cannot move the camera.
			if (!HasSomethingToFrame()) {
				Viewport_StatusText.instance?.ShowStatusText(
					"Nothing selected to frame — select a mesh first.", false, 2f, false);
				return false;
			}
			focus.Focus_Selection_maybe(forceTheFocus: true);
			return true;
		}

		/// <summary>Overview needs at least one selected mesh — that is all <see cref="CameraFocus"/> will frame.</summary>
		public static bool HasSomethingToFrame() {
			var models = ModelsHandler_3D.instance;
			if (models == null) {
				return false;
			}
			var selected = models.selectedMeshes;
			return selected != null && selected.Count > 0;
		}
	}
}
