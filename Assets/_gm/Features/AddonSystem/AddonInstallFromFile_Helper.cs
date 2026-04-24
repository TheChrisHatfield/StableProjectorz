using System;
using System.Collections;
using UnityEngine;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// Connects SimpleFileBrowser to add-on installation the same way other features do
	/// (<see cref="Images_ImportHelper"/>, <see cref="BrushRibbon_UI_AlphaPicker"/>, <see cref="Art2D_IconsUI_List"/>):
	/// <c>SetFilters</c> / <c>SetDefaultFilter</c>, then <c>ShowLoadDialog</c>.
	/// Raises the browser canvas above fullscreen overlays (e.g. add-on manager at sort order 32767).
	/// </summary>
	public static class AddonInstallFromFile_Helper {

		/// <summary>Sort order delta applied on top of the host overlay so the file browser receives clicks.</summary>
		public const int SortOrderOffsetAboveOverlay = 100;

		/// <summary>
		/// Waits one frame (avoids opening the browser on the same pointer-up frame as the button), closes any open dialog, then opens the zip / __init__.py picker.
		/// </summary>
		public static IEnumerator CoDeferredThenPickZipOrInitPy(
			int overlayCanvasSortOrder,
			Action<string> onPickedPath,
			Action onCanceled,
			Action<Exception> onSetupFailed) {
			yield return null;
			if (FileBrowser.IsOpen) {
				FileBrowser.HideDialog(false);
				yield return null;
			}
			try {
				OpenPickZipOrInitPyDialog(overlayCanvasSortOrder, onPickedPath, onCanceled, onSetupFailed);
			} catch (Exception ex) {
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Same as <see cref="CoDeferredThenPickZipOrInitPy"/> but for folder pick (tree with <c>__init__.py</c>).</summary>
		public static IEnumerator CoDeferredThenPickAddonFolder(
			int overlayCanvasSortOrder,
			Action<string> onPickedFolderPath,
			Action onCanceled,
			Action<Exception> onSetupFailed) {
			yield return null;
			if (FileBrowser.IsOpen) {
				FileBrowser.HideDialog(false);
				yield return null;
			}
			try {
				OpenPickAddonFolderDialog(overlayCanvasSortOrder, onPickedFolderPath, onCanceled, onSetupFailed);
			} catch (Exception ex) {
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Pick a <c>.zip</c> or the add-on’s <c>__init__.py</c> (installs the folder that contains it).</summary>
		public static void OpenPickZipOrInitPyDialog(
			int overlayCanvasSortOrder,
			Action<string> onPickedPath,
			Action onCanceled,
			Action<Exception> onSetupFailed) {
			try {
				_ = FileBrowser.Instance;
				FileBrowser.SetFilters(true,
					new FileBrowser.Filter("Add-on (.zip)", "zip"),
					new FileBrowser.Filter("Add-on entry (__init__.py)", "py"));
				FileBrowser.SetDefaultFilter("zip");
				ShowLoadDialogAboveOverlay(overlayCanvasSortOrder, FileBrowser.PickMode.Files,
					"Install add-on (.zip or __init__.py)", "Install",
					onPickedPath, onCanceled);
			} catch (Exception ex) {
				onSetupFailed?.Invoke(ex);
			}
		}

		/// <summary>Pick a folder whose tree contains <c>__init__.py</c> (same rules as zip extract).</summary>
		public static void OpenPickAddonFolderDialog(
			int overlayCanvasSortOrder,
			Action<string> onPickedFolderPath,
			Action onCanceled,
			Action<Exception> onSetupFailed) {
			try {
				_ = FileBrowser.Instance;
				ShowLoadDialogAboveOverlay(overlayCanvasSortOrder, FileBrowser.PickMode.Folders,
					"Install add-on (folder with __init__.py)", "Select",
					onPickedFolderPath, onCanceled);
			} catch (Exception ex) {
				onSetupFailed?.Invoke(ex);
			}
		}

		static void ShowLoadDialogAboveOverlay(
			int overlayCanvasSortOrder,
			FileBrowser.PickMode pickMode,
			string title,
			string submitLabel,
			Action<string> onPickedPath,
			Action onCanceled) {

			Canvas fbCanvas = FileBrowser.Instance != null ? FileBrowser.Instance.GetComponent<Canvas>() : null;
			int prevSort = fbCanvas != null ? fbCanvas.sortingOrder : 0;
			bool prevOverride = fbCanvas != null && fbCanvas.overrideSorting;
			if (fbCanvas != null) {
				fbCanvas.overrideSorting = true;
				fbCanvas.sortingOrder = overlayCanvasSortOrder + SortOrderOffsetAboveOverlay;
			}

			void RestoreCanvas() {
				var inst = FileBrowser.Instance;
				if (inst == null) {
					return;
				}
				var c = inst.GetComponent<Canvas>();
				if (c == null) {
					return;
				}
				c.sortingOrder = prevSort;
				c.overrideSorting = prevOverride;
			}

			FileBrowser.ShowLoadDialog(
				paths => {
					RestoreCanvas();
					if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) {
						onPickedPath?.Invoke(paths[0]);
					} else {
						onCanceled?.Invoke();
					}
				},
				() => {
					RestoreCanvas();
					onCanceled?.Invoke();
				},
				pickMode,
				false,
				null,
				null,
				title,
				submitLabel);
		}
	}
}
