using UnityEngine;

namespace spz {

	/// <summary>
	/// Project-wide uGUI scale helpers: 8px spacing grid and width bands aligned with common Tailwind defaults
	/// (evaluated in <b>canvas reference pixels</b> — same space as <see cref="UnityEngine.UI.CanvasScaler"/> reference resolution).
	/// Use for new runtime-built panels, overlays, and responsive MonoBehaviours so behavior stays consistent and reproducible.
	/// </summary>
	public static class ProjectUiScale {
		public const float SpaceUnit = 8f;

		/// <summary>Spacing in reference pixels: n × 8 (e.g. <c>Space(3) == 24</c> for padding).</summary>
		public static float Space(int n) => n * SpaceUnit;

		/// <summary>Width breakpoints in reference pixels (Tailwind default scale).</summary>
		public const float BreakpointSm = 640f;
		public const float BreakpointMd = 768f;
		public const float BreakpointLg = 1024f;
		public const float BreakpointXl = 1280f;
		public const float Breakpoint2Xl = 1536f;

		public enum Band {
			Xs,
			Sm,
			Md,
			Lg,
			Xl
		}

		public static Band GetBand(float widthRefPx) {
			if (widthRefPx >= BreakpointXl) return Band.Xl;
			if (widthRefPx >= BreakpointLg) return Band.Lg;
			if (widthRefPx >= BreakpointMd) return Band.Md;
			if (widthRefPx >= BreakpointSm) return Band.Sm;
			return Band.Xs;
		}

		public static bool IsAtLeastWidth(float widthRefPx, float breakpointMinWidth) => widthRefPx >= breakpointMinWidth;

		/// <summary>
		/// Sizes a centered modal from an outer rect (e.g. fullscreen blocker): clamp between min/max with symmetric margin.
		/// </summary>
		public static Vector2 ClampModalSize(Rect outer, float maxW, float maxH, float minW, float minH, float margin) {
			float aw = outer.width - margin * 2f;
			float ah = outer.height - margin * 2f;
			float w = Mathf.Clamp(aw, minW, maxW);
			float h = Mathf.Clamp(ah, minH, maxH);
			return new Vector2(w, h);
		}
	}
}
