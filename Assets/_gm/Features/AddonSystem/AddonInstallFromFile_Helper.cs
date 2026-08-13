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
	/// Raises the browser canvas above fullscreen overlays (e.g. add-on manager at sort order 32767)
	/// and temporarily disables that overlay's GraphicRaycaster so clicks reach the browser.
	/// </summary>
	public static class AddonInstallFromFile_Helper {

		/// <summary>Sort order delta applied on top of the host overlay so the file browser receives clicks.</summary>
		public const int SortOrderOffsetAboveOverlay = 100;

		static GraphicRaycaster s_suppressedOverlayRaycaster;

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
			try {
				OpenPickZipOrInitPyDialog(overlayCanvasSortOrder, onPickedPath, onCanceled, onSetupFailed, overlayCanvasToYield);
			} catch (Exception ex) {
				RestoreSuppressedOverlayRaycaster();
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
			try {
				OpenPickAddonFolderDialog(overlayCanvasSortOrder, onPickedFolderPath, onCanceled, onSetupFailed, overlayCanvasToYield);
			} catch (Exception ex) {
				RestoreSuppressedOverlayRaycaster();
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
					onPickedPath, onCanceled, overlayCanvasToYield);
			} catch (Exception ex) {
				RestoreSuppressedOverlayRaycaster();
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
					onPickedFolderPath, onCanceled, overlayCanvasToYield);
			} catch (Exception ex) {
				RestoreSuppressedOverlayRaycaster();
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

		static void SuppressOverlayRaycaster(Canvas overlayCanvas) {
			RestoreSuppressedOverlayRaycaster();
			if (overlayCanvas == null) return;
			var ray = overlayCanvas.GetComponent<GraphicRaycaster>();
			if (ray == null || !ray.enabled) return;
			s_suppressedOverlayRaycaster = ray;
			ray.enabled = false;
		}

		static void RestoreSuppressedOverlayRaycaster() {
			if (s_suppressedOverlayRaycaster != null) {
				s_suppressedOverlayRaycaster.enabled = true;
				s_suppressedOverlayRaycaster = null;
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
			Action onCanceled,
			Canvas overlayCanvasToYield) {

			SuppressOverlayRaycaster(overlayCanvasToYield);
			ElevateFileBrowserCanvas(overlayCanvasSortOrder);

			void Finish(Action next) {
				RestoreSuppressedOverlayRaycaster();
				ElevateFileBrowserCanvas(overlayCanvasSortOrder); // no-op if hidden; keeps sort if reused
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
