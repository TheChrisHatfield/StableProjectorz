using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// Connects SimpleFileBrowser to add-on installation the same way other features do
	/// (<see cref="Images_ImportHelper"/>, <see cref="BrushRibbon_UI_AlphaPicker"/>, <see cref="Art2D_IconsUI_List"/>):
	/// <c>SetFilters</c> / <c>SetDefaultFilter</c>, then <c>ShowLoadDialog</c>.
	/// Raises the browser canvas above fullscreen overlays (e.g. add-on manager at sort order 32767).
	/// Does <b>not</b> disable the manager GraphicRaycaster — that plus FileBrowser's GlobalClickBlocker
	/// deadlocked all UI when the dialog opened behind / failed to take clicks.
	/// </summary>
	public static class AddonInstallFromFile_Helper {

		/// <summary>Sort order delta applied on top of the host overlay so the file browser receives clicks.</summary>
		public const int SortOrderOffsetAboveOverlay = 100;

		/// <summary>
		/// Hide any open install dialog and re-enable Addon Manager canvas raycasts.
		/// Call from ClosePanel / OpenPanel so a stuck FileBrowser GlobalClickBlocker cannot freeze the app.
		/// </summary>
		public static void AbortInstallDialogAndRestoreUi() {
			try {
				if (FileBrowser.IsOpen)
					FileBrowser.HideDialog(false);
			} catch (Exception ex) {
				Debug.LogWarning("[AddonInstallFromFile_Helper] HideDialog: " + ex.Message);
			}
			EnsureAddonManagerCanvasRaycastersEnabled();
		}

		/// <summary>Re-enable GraphicRaycasters on AddonManager_Canvas (safety if an older build disabled them).</summary>
		public static void EnsureAddonManagerCanvasRaycastersEnabled() {
			var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < canvases.Length; i++) {
				var c = canvases[i];
				if (c == null || c.gameObject == null) continue;
				if (c.gameObject.name != "AddonManager_Canvas") continue;
				var rays = c.GetComponents<GraphicRaycaster>();
				for (int r = 0; r < rays.Length; r++) {
					if (rays[r] != null)
						rays[r].enabled = true;
				}
			}
		}

		/// <summary>
		/// Waits one frame (avoids opening the browser on the same pointer-up frame as the button), closes any open dialog, then opens the zip / __init__.py picker.
		/// </summary>
		public static IEnumerator CoDeferredThenPickZipOrInitPy(
			int overlayCanvasSortOrder,
			Action<string> onPickedPath,
			Action onCanceled,
			Action<Exception> onSetupFailed,
			Canvas overlayCanvasToYield = null) {
			yield return null;
			if (FileBrowser.IsOpen) {
				FileBrowser.HideDialog(false);
				yield return null;
			}
			EnsureAddonManagerCanvasRaycastersEnabled();
			try {
				OpenPickZipOrInitPyDialog(overlayCanvasSortOrder, onPickedPath, onCanceled, onSetupFailed, overlayCanvasToYield);
			} catch (Exception ex) {
				AbortInstallDialogAndRestoreUi();
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Same as <see cref="CoDeferredThenPickZipOrInitPy"/> but for folder pick (tree with <c>__init__.py</c>).</summary>
		public static IEnumerator CoDeferredThenPickAddonFolder(
			int overlayCanvasSortOrder,
			Action<string> onPickedFolderPath,
			Action onCanceled,
			Action<Exception> onSetupFailed,
			Canvas overlayCanvasToYield = null) {
			yield return null;
			if (FileBrowser.IsOpen) {
				FileBrowser.HideDialog(false);
				yield return null;
			}
			EnsureAddonManagerCanvasRaycastersEnabled();
			try {
				OpenPickAddonFolderDialog(overlayCanvasSortOrder, onPickedFolderPath, onCanceled, onSetupFailed, overlayCanvasToYield);
			} catch (Exception ex) {
				AbortInstallDialogAndRestoreUi();
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Pick a <c>.zip</c> or the add-on’s <c>__init__.py</c> (installs the folder that contains it).</summary>
		public static void OpenPickZipOrInitPyDialog(
			int overlayCanvasSortOrder,
			Action<string> onPickedPath,
			Action onCanceled,
			Action<Exception> onSetupFailed,
			Canvas overlayCanvasToYield = null) {
			try {
				if (!EnsureFileBrowserInstance(out string deny)) {
					onSetupFailed?.Invoke(new InvalidOperationException(deny));
					return;
				}
				FileBrowser.SetFilters(true,
					new FileBrowser.Filter("Add-on (.zip)", "zip"),
					new FileBrowser.Filter("Add-on entry (__init__.py)", "py"));
				FileBrowser.SetDefaultFilter("zip");
				ShowLoadDialogAboveOverlay(overlayCanvasSortOrder, FileBrowser.PickMode.Files,
					"Install add-on (.zip or __init__.py)", "Install",
					onPickedPath, onCanceled);
			} catch (Exception ex) {
				AbortInstallDialogAndRestoreUi();
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Pick a folder whose tree contains <c>__init__.py</c> (same rules as zip extract).</summary>
		public static void OpenPickAddonFolderDialog(
			int overlayCanvasSortOrder,
			Action<string> onPickedFolderPath,
			Action onCanceled,
			Action<Exception> onSetupFailed,
			Canvas overlayCanvasToYield = null) {
			try {
				if (!EnsureFileBrowserInstance(out string deny)) {
					onSetupFailed?.Invoke(new InvalidOperationException(deny));
					return;
				}
				ShowLoadDialogAboveOverlay(overlayCanvasSortOrder, FileBrowser.PickMode.Folders,
					"Install add-on (folder with __init__.py)", "Select",
					onPickedFolderPath, onCanceled);
			} catch (Exception ex) {
				AbortInstallDialogAndRestoreUi();
				onSetupFailed?.Invoke(ex);
			}
		}

		static bool EnsureFileBrowserInstance(out string denyReason) {
			denyReason = null;
			try {
				var inst = FileBrowser.Instance;
				if (inst == null) {
					denyReason = "SimpleFileBrowserCanvas failed to instantiate (Resources.Load returned null).";
					return false;
				}
				return true;
			} catch (Exception ex) {
				denyReason = "SimpleFileBrowserCanvas missing from build Resources: " + ex.Message;
				return false;
			}
		}

		static void ElevateFileBrowserCanvas(int overlayCanvasSortOrder) {
			var inst = FileBrowser.Instance;
			if (inst == null) return;
			Canvas fbCanvas = inst.GetComponent<Canvas>();
			if (fbCanvas == null)
				fbCanvas = inst.GetComponentInChildren<Canvas>(true);
			if (fbCanvas == null) return;
			fbCanvas.overrideSorting = true;
			fbCanvas.sortingOrder = overlayCanvasSortOrder + SortOrderOffsetAboveOverlay;
			if (fbCanvas.GetComponent<GraphicRaycaster>() == null)
				fbCanvas.gameObject.AddComponent<GraphicRaycaster>();
		}

		static void ShowLoadDialogAboveOverlay(
			int overlayCanvasSortOrder,
			FileBrowser.PickMode pickMode,
			string title,
			string submitLabel,
			Action<string> onPickedPath,
			Action onCanceled) {

			ElevateFileBrowserCanvas(overlayCanvasSortOrder);

			void Finish(Action next) {
				EnsureAddonManagerCanvasRaycastersEnabled();
				next?.Invoke();
			}

			FileBrowser.ShowLoadDialog(
				paths => {
					Finish(() => {
						if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
							onPickedPath?.Invoke(paths[0]);
						else
							onCanceled?.Invoke();
					});
				},
				() => {
					Finish(() => onCanceled?.Invoke());
				},
				pickMode,
				false,
				null,
				null,
				title,
				submitLabel);

			// Show() activates the canvas after our first elevate — assert again so we stay above the manager.
			ElevateFileBrowserCanvas(overlayCanvasSortOrder);
		}
	}
}
