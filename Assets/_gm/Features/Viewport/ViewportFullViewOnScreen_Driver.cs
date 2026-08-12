using System;
using UnityEngine;

namespace spz {

	/// <summary>
	/// On-screen full view: hides the skeleton <b>left</b> (SD) and <b>right</b> (command strip) columns so the
	/// <b>center viewport</b> is the only major region, with the viewport's inner <b>left/right vertical ribbons</b> still visible.
	/// Pair with <see cref="MainViewport_UI"/> (between-ribbons placement), <see cref="InnerViewport_SizeReference"/>, and <see cref="View_UserCamera"/>.
	/// </summary>
	public static class ViewportFullViewOnScreen_Driver {

		public static bool IsActive { get; private set; }

		/// <summary>True when the skeleton <b>left</b> column is collapsed (width 0): center-only full view, or right-only (paint) mode.</summary>
		public static bool IsSkeletonLeftColumnHidden() {
			var sk = Global_Skeleton_UI.instance;
			if (sk != null && sk.TryGetSidePanelVisibility(out bool left, out _)) {
				return !left;
			}
			return false;
		}

		/// <summary>True for center-only (<see cref="IsActive"/>) or any mode where the left column is still hidden (e.g. open-right).</summary>
		public static bool ShouldHideMirroredLeftColumnContent() {
			return IsActive || IsSkeletonLeftColumnHidden();
		}

		/// <summary>When the left column is hidden, the main viewport should keep "between inner ribbons" fitting (not full skeleton slot).</summary>
		public static bool ShouldUseBetweenRibbonsMainViewportPlacement() {
			return IsActive || IsSkeletonLeftColumnHidden();
		}

		static bool _savedLeft = true;
		static bool _savedRight = true;
		static bool _capturedSave;
		static int _savedGenWidth = 512;
		static int _savedGenHeight = 512;
		static bool _capturedGenResolution;

		public static event Action<bool> ActiveChanged;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetFullViewDriverStatics() {
			// Enter Play Mode Options can disable domain reload — IsActive / captured gen W×H would stick.
			IsActive = false;
			_savedLeft = true;
			_savedRight = true;
			_capturedSave = false;
			_savedGenWidth = 512;
			_savedGenHeight = 512;
			_capturedGenResolution = false;
			ActiveChanged = null;
		}

		public static void SyncFromCurrentSkeleton() {
			var sk = Global_Skeleton_UI.instance;
			if (sk == null || !sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				return;
			}
			bool want = !left && !right;
			// Do not clear _capturedSave when only the right column is open (!left && right) — that is still
			// part of the same fullscreen session; clearing would break TryExit() back to the pre-fullscreen layout.
			if (left) {
				// Leaving FULL SRN by showing the left column (not via TryExit) must still restore SD W/H.
				// Otherwise monitor-sized gen resolution sticks after the session ends.
				if (_capturedGenResolution)
					RestoreSdInputResolutionIfCaptured();
				_capturedSave = false;
			}
			if (IsActive == want) {
				return;
			}
			IsActive = want;
			ActiveChanged?.Invoke(want);
		}

		public static bool TryEnter() {
			var sk = Global_Skeleton_UI.instance;
			if (sk == null || !sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				return false;
			}
			if (!IsActive) {
				_savedLeft = left;
				_savedRight = right;
				_capturedSave = true;
				CaptureAndApplyScreenResolutionToSdInputs();
			}
			if (!sk.SetSidePanelVisibility(false, false)) {
				return false;
			}
			SyncFromCurrentSkeleton();
			return IsActive;
		}

		public static bool TryExit() {
			var sk = Global_Skeleton_UI.instance;
			if (sk == null) {
				return false;
			}
			bool left = _capturedSave ? _savedLeft : true;
			bool right = _capturedSave ? _savedRight : true;
			if (!sk.SetSidePanelVisibility(left, right)) {
				return false;
			}
			RestoreSdInputResolutionIfCaptured();
			_capturedSave = false;
			SyncFromCurrentSkeleton();
			return true;
		}

		static void CaptureAndApplyScreenResolutionToSdInputs() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				_capturedGenResolution = false;
				return;
			}
			_savedGenWidth = Mathf.Max(64, sd.width);
			_savedGenHeight = Mathf.Max(64, sd.height);
			_capturedGenResolution = true;
			// Apply monitor resolution immediately on enter so the SD width/height fields update on the
			// first frame even if the bridge's deferred NotifyLayoutRefreshedForPendingGenRefit() is missed
			// (e.g. early-return paths, panel inactive at schedule time, or coroutine never starts).
			// The deferred ScheduleFullSrnScreenResolutionToSdInputsNextFrame still runs as a layout-settle pass.
			ApplyFullSrnScreenResolutionToSdInputs();
		}

		static void RestoreSdInputResolutionIfCaptured() {
			if (!_capturedGenResolution) {
				return;
			}
			var sd = SD_InputPanel_UI.instance;
			if (sd != null) {
				sd.SetWidthHeight(_savedGenWidth, _savedGenHeight);
			}
			_capturedGenResolution = false;
		}

		static bool _loggedResolveBestOnce;

		/// <summary>
		/// Native pixel size of the display the game window is on (paired W×H — never Max width from one
		/// monitor with Max height from another). Falls back to window / currentResolution.
		/// </summary>
		static Vector2Int ResolveBestScreenPixelSize() {
			int w = 0;
			int h = 0;

			// Primary on Unity 6: native pixel size of the display the game window is currently on.
			try {
				DisplayInfo di = Screen.mainWindowDisplayInfo;
				if (di.width > 0 && di.height > 0) {
					w = di.width;
					h = di.height;
				}
			}
			catch { }

			// Same-window fallbacks only — do not Max W/H across every connected monitor
			// (multi-monitor frankenstein e.g. 3840×3440 that matches no screen).
			if (w < 64 || h < 64) {
				if (Screen.width >= 64 && Screen.height >= 64) {
					w = Screen.width;
					h = Screen.height;
				}
			}
			if (w < 64 || h < 64) {
				Resolution scr = Screen.currentResolution;
				if (scr.width >= 64 && scr.height >= 64) {
					w = scr.width;
					h = scr.height;
				}
			}
			if (w < 64 || h < 64) {
				w = Mathf.Max(64, w > 0 ? w : 1920);
				h = Mathf.Max(64, h > 0 ? h : 1080);
			}

			if (!_loggedResolveBestOnce) {
				_loggedResolveBestOnce = true;
				int diW = 0, diH = 0;
				try { var di = Screen.mainWindowDisplayInfo; diW = di.width; diH = di.height; } catch { }
				Resolution scrLog = Screen.currentResolution;
				Debug.Log(
					$"[ViewportFullViewOnScreen_Driver] ResolveBestScreenPixelSize → {w}x{h} | " +
					$"mainWindowDisplayInfo={diW}x{diH} | " +
					$"Screen.currentResolution={scrLog.width}x{scrLog.height} | " +
					$"Screen.width/height={Screen.width}x{Screen.height}");
			}
			return new Vector2Int(w, h);
		}

		static bool _loggedFullSrnApplyOnce;
		static bool _loggedOpenRightApplyOnce;
		static bool _loggedOpenRightSourceOnce;

		static bool TryGetRectScreenPixelSize(RectTransform rt, out Vector2 sizePx) {
			sizePx = Vector2.zero;
			if (rt == null) {
				return false;
			}
			var canvas = rt.GetComponentInParent<Canvas>();
			Camera cam = null;
			if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) {
				cam = canvas.worldCamera;
			}

			// 1) Preferred: world corners projected into screen pixels using the effective UI camera.
			Vector3[] corners = new Vector3[4];
			rt.GetWorldCorners(corners);
			Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
			Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
			float worldW = Mathf.Abs(tr.x - bl.x);
			float worldH = Mathf.Abs(tr.y - bl.y);
			if (worldW >= 1f && worldH >= 1f) {
				sizePx = new Vector2(worldW, worldH);
				return true;
			}

			// 2) Fallback: rect size times root-canvas scale factor.
			float scale = 1f;
			if (canvas != null) {
				var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
				scale = Mathf.Max(0.0001f, root.scaleFactor);
			}
			float rectW = Mathf.Abs(rt.rect.width * scale);
			float rectH = Mathf.Abs(rt.rect.height * scale);
			if (rectW >= 1f && rectH >= 1f) {
				sizePx = new Vector2(rectW, rectH);
				return true;
			}
			return false;
		}

		/// <summary>
		/// <b>FULL SRN path</b> — set SD W/H from monitor <see cref="Screen.currentResolution"/> (fallback: <see cref="Screen.width"/>/height).
		/// Not the game window; only used for on-screen full view entry, not for OPEN RIGHT.
		/// </summary>
		public static void ApplyFullSrnScreenResolutionToSdInputs() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				return;
			}
			Vector2Int screenPx = ResolveBestScreenPixelSize();
			int w = screenPx.x;
			int h = screenPx.y;
			if (!_loggedFullSrnApplyOnce) {
				_loggedFullSrnApplyOnce = true;
				Debug.Log($"[ViewportFullViewOnScreen_Driver] ApplyFullSrnScreenResolutionToSdInputs → SetWidthHeight({w}, {h})");
			}
			sd.SetWidthHeight(w, h);
		}

		/// <summary>
		/// <b>OPEN RIGHT path</b> — use the current main viewport slot pixel size (canvas region available while right
		/// panel is open), not monitor size.
		/// </summary>
		public static void ApplyOpenRightMainSlotResolutionToSdInputs() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				return;
			}
			Vector2 slotPx = Vector2.zero;
			string source = "none";
			var mainVp = MainViewport_UI.instance;
			if (mainVp != null) {
				if (mainVp.mainViewportRect != null && TryGetRectScreenPixelSize(mainVp.mainViewportRect, out slotPx)) {
					source = "mainViewportRect(screen)";
				}
				if (slotPx.sqrMagnitude < 16f && mainVp.innerViewportRect != null && TryGetRectScreenPixelSize(mainVp.innerViewportRect, out slotPx)) {
					source = "innerViewportRect(screen)";
				}
			}
			if (slotPx.sqrMagnitude < 16f) {
				Vector2Int screenPx = ResolveBestScreenPixelSize();
				slotPx = new Vector2(screenPx.x, screenPx.y);
				source = "screenFallback";
			}

			int w = Mathf.Max(64, Mathf.RoundToInt(slotPx.x));
			int h = Mathf.Max(64, Mathf.RoundToInt(slotPx.y));
			if (!_loggedOpenRightSourceOnce) {
				_loggedOpenRightSourceOnce = true;
				Debug.Log($"[ViewportFullViewOnScreen_Driver] OPEN RIGHT size source={source}, slotPx={slotPx.x:0.##}x{slotPx.y:0.##}");
			}
			if (!_loggedOpenRightApplyOnce) {
				_loggedOpenRightApplyOnce = true;
				Debug.Log($"[ViewportFullViewOnScreen_Driver] ApplyOpenRightMainSlotResolutionToSdInputs → SetWidthHeight({w}, {h})");
			}
			sd.SetWidthHeight(w, h);
		}

		/// <summary>
		/// True after <see cref="TryEnter"/> captured the user's SD W/H so FULL SRN / OPEN RIGHT may temporarily rewrite them.
		/// Without this, collapsing the left column (paint / open-right) must not silently replace classic 512/1024 presets — that path does not exist in OG and makes Gen Art look "off".
		/// </summary>
		public static bool HasCapturedGenResolutionForFullViewSession => _capturedGenResolution;

		/// <summary>
		/// Single adaptive resolver to prevent FULL SRN vs OPEN RIGHT ping-pong:
		/// center-only (!left && !right) => monitor resolution,
		/// open-right (!left && right) => viewport slot size.
		/// Only runs inside an explicit FULL SRN session that captured the prior SD size (see <see cref="TryEnter"/>).
		/// </summary>
		public static void ApplyAdaptiveResolutionToSdInputsForCurrentSideState() {
			// OG never rewrote SD W/H from layout. Fork FULL SRN may, but only after capturing the user's presets.
			if (!_capturedGenResolution) {
				return;
			}
			var sk = Global_Skeleton_UI.instance;
			if (sk == null || !sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				return;
			}
			if (!left && !right) {
				ApplyFullSrnScreenResolutionToSdInputs();
				return;
			}
			if (!left && right) {
				ApplyOpenRightMainSlotResolutionToSdInputs();
			}
		}

		/// <summary>
		/// Call after <see cref="Global_Skeleton_UI.ForceLayoutRefreshAfterPanelResize"/> in the same flow as
		/// on-screen full-view <see cref="TryEnter"/> (FULL SRN). Gated on <see cref="_capturedSave"/>.
		/// </summary>
		public static void NotifyLayoutRefreshedForPendingGenRefit() {
			if (!_capturedSave) {
				return;
			}
			// Exit path clears _capturedSave before this runs; only enter (or in-session) layout passes here.
			ScheduleAdaptiveResolutionToSdInputsNextFrame();
		}

		/// <summary>Deferred <see cref="ApplyFullSrnScreenResolutionToSdInputs"/> (FULL SRN only).</summary>
		public static void ScheduleFullSrnScreenResolutionToSdInputsNextFrame() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				ApplyFullSrnScreenResolutionToSdInputs();
				return;
			}
			sd.ScheduleFullSrnScreenResolutionApplyNextFrame();
		}

		/// <summary>Deferred <see cref="ApplyOpenRightMainSlotResolutionToSdInputs"/> (OPEN RIGHT only).</summary>
		public static void ScheduleOpenRightMainSlotGenResolutionToSdInputsNextFrame() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				ApplyOpenRightMainSlotResolutionToSdInputs();
				return;
			}
			sd.ScheduleOpenRightMainSlotGenResolutionNextFrame();
		}

		/// <summary>Deferred adaptive apply for the current side state (FULL SRN vs OPEN RIGHT).</summary>
		public static void ScheduleAdaptiveResolutionToSdInputsNextFrame() {
			var sd = SD_InputPanel_UI.instance;
			if (sd == null) {
				ApplyAdaptiveResolutionToSdInputsForCurrentSideState();
				return;
			}
			sd.ScheduleAdaptiveResolutionFromViewportModeNextFrame();
		}
	}
}
