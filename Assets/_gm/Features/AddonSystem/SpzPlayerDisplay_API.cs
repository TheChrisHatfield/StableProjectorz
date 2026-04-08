using UnityEngine;

namespace spz {

	/// <summary>
	/// Player window / OS fullscreen toggles for add-on JSON-RPC (no MonoBehaviour; no FastPath dependency).
	/// Exclusive fullscreen is supported on Windows builds; other platforms may fall back per Unity.
	/// </summary>
	public static class SpzPlayerDisplay_API {

		/// <summary>Fills missing dimension when only one of width/height is set (e.g. JSON-RPC omitted a key).</summary>
		public static void NormalizeResolutionPair(ref int w, ref int h) {
			if (w > 0 && h <= 0) {
				h = Screen.height;
			}
			if (h > 0 && w <= 0) {
				w = Screen.width;
			}
		}

		public static void GetScreenState(out bool fullscreen, out FullScreenMode mode, out int width, out int height) {
			fullscreen = Screen.fullScreen;
			mode = Screen.fullScreenMode;
			width = Screen.width;
			height = Screen.height;
		}

		public static bool IsExclusiveFullScreen(FullScreenMode mode) {
			return mode == FullScreenMode.ExclusiveFullScreen;
		}

		/// <summary>Best-effort primary monitor size; falls back to <see cref="Screen.currentResolution"/>.</summary>
		public static void GetPrimaryDisplaySize(out int w, out int h) {
			w = 0;
			h = 0;
			try {
				w = Display.main.systemWidth;
				h = Display.main.systemHeight;
			}
			catch {
				// No displays / some headless or batch contexts
			}
			if (w <= 0 || h <= 0) {
				var r = Screen.currentResolution;
				w = r.width;
				h = r.height;
			}
		}

		/// <summary>
		/// Apply window mode. If width/height &gt; 0, calls <see cref="Screen.SetResolution"/>; otherwise toggles mode only.
		/// </summary>
		public static bool SetWindowed(int w, int h) {
			if (Application.isBatchMode) {
				return false;
			}
			NormalizeResolutionPair(ref w, ref h);
			if (w > 0 && h > 0) {
				Screen.SetResolution(w, h, FullScreenMode.Windowed);
			}
			else {
				Screen.fullScreenMode = FullScreenMode.Windowed;
				Screen.fullScreen = false;
			}
			return true;
		}

		public static bool SetBorderlessFullScreen(int w, int h) {
			if (Application.isBatchMode) {
				return false;
			}
			NormalizeResolutionPair(ref w, ref h);
			if (w > 0 && h > 0) {
				Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
			}
			else {
				Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
				Screen.fullScreen = true;
			}
			return true;
		}

		/// <summary>
		/// OS-style exclusive fullscreen (Windows standalone; editor/other platforms may differ).
		/// </summary>
		public static bool SetExclusiveFullScreen(int w, int h, int refreshRateHz) {
			if (Application.isBatchMode) {
				return false;
			}
			NormalizeResolutionPair(ref w, ref h);
			if (w <= 0 || h <= 0) {
				Screen.fullScreenMode = FullScreenMode.ExclusiveFullScreen;
				Screen.fullScreen = true;
				return true;
			}

			if (refreshRateHz > 0) {
				var rr = new RefreshRate { numerator = (uint)refreshRateHz, denominator = 1 };
				Screen.SetResolution(w, h, FullScreenMode.ExclusiveFullScreen, rr);
			}
			else {
				Screen.SetResolution(w, h, FullScreenMode.ExclusiveFullScreen);
			}
			return true;
		}
	}
}
