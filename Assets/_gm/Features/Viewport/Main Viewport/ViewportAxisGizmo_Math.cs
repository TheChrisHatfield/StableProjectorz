using UnityEngine;

namespace spz {

	/// <summary>
	/// Projection math for the viewport orientation gizmo (Blender / 3ds Max style axis balls in the
	/// top-right of the 3D view). Pure functions only — no scene lookups — so EditMode tests can assert
	/// handle placement, depth sorting and snap poses without a camera rig.
	/// </summary>
	public static class ViewportAxisGizmo_Math {

		/// <summary>+X, -X, +Y, -Y, +Z, -Z in world space (draw + hit order of the six handles).</summary>
		public static readonly Vector3[] AxisDirections = {
			Vector3.right, Vector3.left,
			Vector3.up, Vector3.down,
			Vector3.forward, Vector3.back,
		};

		public static readonly Color AxisColorX = new Color(0.90f, 0.29f, 0.33f, 1f);
		public static readonly Color AxisColorY = new Color(0.47f, 0.78f, 0.27f, 1f);
		public static readonly Color AxisColorZ = new Color(0.26f, 0.55f, 0.93f, 1f);

		/// <summary>Offset of a handle from the gizmo center, in UI px, for a world axis seen through <paramref name="cameraRotation"/>.</summary>
		public static Vector2 AxisHandleOffset(Quaternion cameraRotation, Vector3 worldAxis, float radiusPx) {
			Vector3 camSpace = Quaternion.Inverse(cameraRotation) * worldAxis.normalized;
			return new Vector2(camSpace.x, camSpace.y) * radiusPx;
		}

		/// <summary>0 = axis points away from the viewer (behind the gizmo), 1 = axis points straight at the viewer.</summary>
		public static float TowardsViewer01(Quaternion cameraRotation, Vector3 worldAxis) {
			Vector3 camSpace = Quaternion.Inverse(cameraRotation) * worldAxis.normalized;
			return Mathf.Clamp01((-camSpace.z + 1f) * 0.5f);
		}

		public static float HandleScale(float towardsViewer01) =>
			Mathf.Lerp(0.66f, 1f, Mathf.Clamp01(towardsViewer01));

		public static float HandleAlpha(float towardsViewer01, bool isPositiveAxis) {
			float t = Mathf.Clamp01(towardsViewer01);
			return isPositiveAxis ? Mathf.Lerp(0.45f, 1f, t) : Mathf.Lerp(0.30f, 0.85f, t);
		}

		/// <summary>Sibling index weight: larger draws on top, so handles nearer the viewer overlap the far ones.</summary>
		public static int DrawOrderKey(float towardsViewer01) =>
			Mathf.RoundToInt(Mathf.Clamp01(towardsViewer01) * 10000f);

		/// <summary>True for +X / +Y / +Z (labelled, filled balls); negatives draw as dim rings.</summary>
		public static bool IsPositiveAxis(Vector3 worldAxis) =>
			worldAxis.x > 0.5f || worldAxis.y > 0.5f || worldAxis.z > 0.5f;

		public static string AxisLabel(Vector3 worldAxis) {
			if (Mathf.Abs(worldAxis.x) > 0.5f) return "X";
			if (Mathf.Abs(worldAxis.y) > 0.5f) return "Y";
			return "Z";
		}

		public static Color AxisColor(Vector3 worldAxis) {
			if (Mathf.Abs(worldAxis.x) > 0.5f) return AxisColorX;
			if (Mathf.Abs(worldAxis.y) > 0.5f) return AxisColorY;
			return AxisColorZ;
		}

		/// <summary>Screen-up reference when snapping. Top / bottom views cannot use world up, so they fall back to ±Z.</summary>
		public static Vector3 UpHintForAxis(Vector3 worldAxis) {
			Vector3 axis = worldAxis.normalized;
			if (axis.y > 0.99f) return Vector3.forward;
			if (axis.y < -0.99f) return Vector3.back;
			return Vector3.up;
		}

		/// <summary><paramref name="worldAxis"/> is the direction from the pivot towards the camera, so the camera looks back along -axis.</summary>
		public static Quaternion CameraRotationForAxis(Vector3 worldAxis) {
			Vector3 axis = worldAxis.normalized;
			return Quaternion.LookRotation(-axis, UpHintForAxis(axis));
		}

		public static Vector3 CameraPositionForAxis(Vector3 pivot, Vector3 worldAxis, float distance) =>
			pivot + worldAxis.normalized * Mathf.Max(0.01f, distance);

		/// <summary>Keeps the current framing when snapping: distance from the pivot is preserved, with a floor for degenerate poses.</summary>
		public static float SnapDistance(Vector3 cameraPosition, Vector3 pivot, float minDistance = 0.25f) =>
			Mathf.Max(minDistance, Vector3.Distance(cameraPosition, pivot));
	}
}
