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

		static bool _savedLeft = true;
		static bool _savedRight = true;
		static bool _capturedSave;
		static int _savedGenWidth = 512;
		static int _savedGenHeight = 512;
		static bool _capturedGenResolution;

		public static event Action<bool> ActiveChanged;

		public static void SyncFromCurrentSkeleton() {
			var sk = Global_Skeleton_UI.instance;
			if (sk == null || !sk.TryGetSidePanelVisibility(out bool left, out bool right)) {
				return;
			}
			bool want = !left && !right;
			if (!want) {
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

			Resolution scr = Screen.currentResolution;
			int targetW = Mathf.Max(64, scr.width);
			int targetH = Mathf.Max(64, scr.height);
			sd.SetWidthHeight(targetW, targetH);
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
	}
}
