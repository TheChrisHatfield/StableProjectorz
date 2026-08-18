using UnityEngine;

namespace spz {

	/// <summary>
	/// Projection math for the viewport orientation gizmo (top-right of the 3D view). Pure functions only —
	/// no scene lookups — so EditMode tests can assert handle placement, depth sorting and snap poses
	/// without a camera rig. Visual colors live in <see cref="ViewportAxisGizmo_Palette"/> (SPZ chrome,
	/// not Blender primary RGB balls).
	/// </summary>
	public static class ViewportAxisGizmo_Math {

		/// <summary>+X, -X, +Y, -Y, +Z, -Z in world space (draw + hit order of the six handles).</summary>
		public static readonly Vector3[] AxisDirections = {
			Vector3.right, Vector3.left,
			Vector3.up, Vector3.down,
			Vector3.forward, Vector3.back,
		};

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

		/// <summary>True for +X / +Y / +Z (labelled, filled discs); negatives draw as dim rings.</summary>
		public static bool IsPositiveAxis(Vector3 worldAxis) =>
			worldAxis.x > 0.5f || worldAxis.y > 0.5f || worldAxis.z > 0.5f;

		public static string AxisLabel(Vector3 worldAxis) {
			if (Mathf.Abs(worldAxis.x) > 0.5f) return "X";
			if (Mathf.Abs(worldAxis.y) > 0.5f) return "Y";
			return "Z";
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

	/// <summary>
	/// Visual chrome for the orientation gizmo. Default is StableProjectorz cool-grey + sky accent (letter
	/// labels carry axis identity — not Blender R/G/B balls). Nomad builds from active theme tokens (charcoal + gold).
	/// </summary>
	public readonly struct ViewportAxisGizmo_Palette {

		public readonly Color Backdrop;
		public readonly Color PositiveIdle;
		public readonly Color NegativeIdle;
		public readonly Color FacingAccent;
		public readonly Color Stem;
		public readonly Color LabelInk;
		public readonly Color CenterTint;

		public ViewportAxisGizmo_Palette(
			Color backdrop,
			Color positiveIdle,
			Color negativeIdle,
			Color facingAccent,
			Color stem,
			Color labelInk,
			Color centerTint) {
			Backdrop = backdrop;
			PositiveIdle = positiveIdle;
			NegativeIdle = negativeIdle;
			FacingAccent = facingAccent;
			Stem = stem;
			LabelInk = labelInk;
			CenterTint = centerTint;
		}

		/// <summary>Authored SPZ look — cool graphite discs, sky-blue depth cue, soft grey lantern.</summary>
		public static ViewportAxisGizmo_Palette SpzDefault => new ViewportAxisGizmo_Palette(
			backdrop: new Color(0.10f, 0.11f, 0.14f, 0.42f),
			positiveIdle: new Color(0.42f, 0.45f, 0.52f, 1f),
			negativeIdle: new Color(0.28f, 0.30f, 0.34f, 1f),
			facingAccent: new Color(0.30f, 0.60f, 1.00f, 1f), // SpzUiThemeOps default accent
			stem: new Color(0.55f, 0.58f, 0.64f, 1f),
			labelInk: new Color(0.94f, 0.95f, 0.97f, 1f),
			centerTint: new Color(0.72f, 0.74f, 0.78f, 0.70f)
		);

		/// <summary>Nomad-inspired: charcoal discs, gold facing cue, warm sand lantern (from active theme tokens).</summary>
		public static ViewportAxisGizmo_Palette FromThemeTokens(SpzUiThemeOps.ThemeTokens t) {
			if (t == null) {
				return SpzDefault;
			}
			Color backdrop = t.panelBg;
			backdrop.a = Mathf.Clamp(backdrop.a, 0.35f, 0.72f);
			Color center = Color.Lerp(t.iconTint, t.accent, 0.28f);
			center.a = 0.78f;
			return new ViewportAxisGizmo_Palette(
				backdrop: backdrop,
				positiveIdle: t.controlBg,
				negativeIdle: Color.Lerp(t.fieldBg, t.controlBg, 0.45f),
				facingAccent: t.accent,
				stem: Color.Lerp(t.border, t.textMuted, 0.55f),
				labelInk: t.textPrimary,
				centerTint: center
			);
		}

		/// <summary>Handle fill/ring color: idle chrome lerps toward the facing accent as the axis comes toward the viewer.</summary>
		public Color HandleColor(bool positive, float towardsViewer01) {
			Color idle = positive ? PositiveIdle : NegativeIdle;
			float t = Mathf.Clamp01(towardsViewer01);
			Color c = Color.Lerp(idle, FacingAccent, t * (positive ? 0.55f : 0.35f));
			c.a = ViewportAxisGizmo_Math.HandleAlpha(t, positive);
			return c;
		}

		public Color StemColor(float towardsViewer01) {
			Color c = Color.Lerp(Stem, FacingAccent, Mathf.Clamp01(towardsViewer01) * 0.4f);
			c.a = ViewportAxisGizmo_Math.HandleAlpha(towardsViewer01, true) * 0.75f;
			return c;
		}

		public Color LabelColor(float towardsViewer01) {
			Color c = LabelInk;
			c.a = ViewportAxisGizmo_Math.HandleAlpha(towardsViewer01, true);
			return c;
		}
	}
}
