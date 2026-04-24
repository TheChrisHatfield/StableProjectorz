using UnityEngine;

namespace spz {

	/// <summary>
	/// Player window / OS fullscreen toggles for add-on JSON-RPC (no MonoBehaviour; no FastPath dependency).
	/// Exclusive fullscreen is supported on Windows builds; other platforms may fall back per Unity.
	/// Entering fullscreen from windowed snapshots the current window size; leaving fullscreen restores that snapshot (or last explicit windowed resolution from RPC).
	/// </summary>
	public static class SpzPlayerDisplay_API {

		const string PrefsPreferredWindowW = "SpzUserPreferredWindowWidth";
		const string PrefsPreferredWindowH = "SpzUserPreferredWindowHeight";
		const string PrefsPreferredWindowValid = "SpzUserPreferredWindowValid";

		const int kFallbackWindowedW = 1920;
		const int kFallbackWindowedH = 1080;
		// Reject invalid sizes; thresholds low enough that narrow windowed layouts still snapshot before fullscreen.
		const int kMinReasonableWindowW = 200;
		const int kMinReasonableWindowH = 120;

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

		/// <summary>True when the app is in a resizable window (not borderless/exclusive fullscreen).</summary>
		public static bool IsWindowedMode() {
			return Screen.fullScreenMode == FullScreenMode.Windowed;
		}

		/// <summary>
		/// Persists the current window pixel size before switching to fullscreen so <see cref="SetWindowed"/> can restore it later.
		/// </summary>
		public static void SavePreferredWindowFromCurrentIfWindowed() {
			if (Application.isBatchMode) {
				return;
			}
			if (!IsWindowedMode()) {
				return;
			}
			int w = Screen.width;
			int h = Screen.height;
			if (w < kMinReasonableWindowW || h < kMinReasonableWindowH) {
				return;
			}
			PlayerPrefs.SetInt(PrefsPreferredWindowW, w);
			PlayerPrefs.SetInt(PrefsPreferredWindowH, h);
			PlayerPrefs.SetInt(PrefsPreferredWindowValid, 1);
			PlayerPrefs.Save();
		}

		/// <summary>Last saved windowed width/height (from before fullscreen or from an explicit <see cref="SetWindowed"/> call).</summary>
		public static bool TryGetPreferredWindowedSize(out int w, out int h) {
			w = PlayerPrefs.GetInt(PrefsPreferredWindowW, 0);
			h = PlayerPrefs.GetInt(PrefsPreferredWindowH, 0);
			if (PlayerPrefs.GetInt(PrefsPreferredWindowValid, 0) == 0) {
				return false;
			}
			return w >= kMinReasonableWindowW && h >= kMinReasonableWindowH;
		}

		static void PersistPreferredWindow(int w, int h) {
			if (w < kMinReasonableWindowW || h < kMinReasonableWindowH) {
				return;
			}
			PlayerPrefs.SetInt(PrefsPreferredWindowW, w);
			PlayerPrefs.SetInt(PrefsPreferredWindowH, h);
			PlayerPrefs.SetInt(PrefsPreferredWindowValid, 1);
			PlayerPrefs.Save();
		}

		static void ResolveFullscreenDimensions(ref int w, ref int h) {
			NormalizeResolutionPair(ref w, ref h);
			if (w > 0 && h > 0) {
				return;
			}
			GetPrimaryDisplaySize(out w, out h);
			if (w <= 0 || h <= 0) {
				w = kFallbackWindowedW;
				h = kFallbackWindowedH;
			}
		}

		static void ResolveRestoreWindowedDimensions(ref int w, ref int h) {
			NormalizeResolutionPair(ref w, ref h);
			if (w > 0 && h > 0) {
				return;
			}
			if (TryGetPreferredWindowedSize(out w, out h)) {
				return;
			}
			// Avoid resizing an existing windowed session to the hardcoded fallback when prefs were never set (e.g. first run).
			if (IsWindowedMode() && Screen.width >= kMinReasonableWindowW && Screen.height >= kMinReasonableWindowH) {
				w = Screen.width;
				h = Screen.height;
				return;
			}
			w = kFallbackWindowedW;
			h = kFallbackWindowedH;
		}

		/// <summary>
		/// Windowed mode. Omitted or zero width/height restores the last user window size (saved prefs or fallback).
		/// </summary>
		public static bool SetWindowed(int w, int h) {
			if (Application.isBatchMode) {
				return false;
			}
			ResolveRestoreWindowedDimensions(ref w, ref h);
			Screen.SetResolution(w, h, FullScreenMode.Windowed);
			PersistPreferredWindow(w, h);
			return true;
		}

		/// <summary>
		/// Borderless fullscreen on the primary display. Omitted or zero dimensions use <see cref="GetPrimaryDisplaySize"/>.
		/// </summary>
		public static bool SetBorderlessFullScreen(int w, int h) {
			if (Application.isBatchMode) {
				return false;
			}
			SavePreferredWindowFromCurrentIfWindowed();
			ResolveFullscreenDimensions(ref w, ref h);
			Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
			return true;
		}

		/// <summary>
		/// OS-style exclusive fullscreen (Windows standalone; editor/other platforms may differ).
		/// Omitted or zero dimensions use the primary monitor size.
		/// </summary>
		public static bool SetExclusiveFullScreen(int w, int h, int refreshRateHz) {
			if (Application.isBatchMode) {
				return false;
			}
			SavePreferredWindowFromCurrentIfWindowed();
			ResolveFullscreenDimensions(ref w, ref h);

			if (refreshRateHz > 0) {
				var rr = new RefreshRate { numerator = (uint)refreshRateHz, denominator = 1 };
				Screen.SetResolution(w, h, FullScreenMode.ExclusiveFullScreen, rr);
			}
			else {
				Screen.SetResolution(w, h, FullScreenMode.ExclusiveFullScreen);
			}
			return true;
		}

		/// <summary>
		/// Switches between windowed (restored user size) and borderless fullscreen (primary monitor resolution).
		/// </summary>
		public static bool ToggleBorderlessFullscreenPreferMonitor() {
			if (Application.isBatchMode) {
				return false;
			}
			if (IsWindowedMode()) {
				return SetBorderlessFullScreen(0, 0);
			}
			return SetWindowed(0, 0);
		}
	}
}
