using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using SimpleFileBrowser;

namespace spz {

	/// <summary>
	/// Install-from-file picker for Add-on Manager.
	/// On Windows (Editor + player), uses the OS file dialog so Install is never buried under
	/// AddonManager_Canvas (sort 32767). Elsewhere: parks the manager overlay and uses SimpleFileBrowser.
	/// Never disables the manager GraphicRaycaster (that + FileBrowser GlobalClickBlocker freezes all clicks).
	/// </summary>
	public static class AddonInstallFromFile_Helper {

		public const int SortOrderOffsetAboveOverlay = 100;

		static GameObject s_parkedManagerOverlay;

		/// <summary>
		/// Hide any open install dialog and restore Addon Manager overlay visibility / raycasters.
		/// </summary>
		public static void AbortInstallDialogAndRestoreUi() {
			try {
				if (FileBrowser.IsOpen)
					FileBrowser.HideDialog(false);
			} catch (Exception ex) {
				Debug.LogWarning("[AddonInstallFromFile_Helper] HideDialog: " + ex.Message);
			}
			UnparkManagerOverlay();
			EnsureAddonManagerCanvasRaycastersEnabled();
		}

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

		static void ParkManagerOverlay(Canvas overlayCanvas) {
			UnparkManagerOverlay();
			if (overlayCanvas == null) return;
			s_parkedManagerOverlay = overlayCanvas.gameObject;
			s_parkedManagerOverlay.SetActive(false);
			Debug.Log("[AddonInstallFromFile_Helper] Parked AddonManager_Canvas for file browser.");
		}

		static void UnparkManagerOverlay() {
			if (s_parkedManagerOverlay == null) return;
			s_parkedManagerOverlay.SetActive(true);
			s_parkedManagerOverlay = null;
			Debug.Log("[AddonInstallFromFile_Helper] Restored AddonManager_Canvas.");
		}

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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			// Native OS dialog is the reliable Install path under the Add-on Manager modal.
			Debug.Log("[AddonInstallFromFile_Helper] Opening native Windows install file dialog…");
			bool nativeOk = false;
			string nativePath = null;
			string nativeErr = null;
			try {
				nativeOk = TryNativeWindowsOpenZipOrInitPy(out nativePath, out nativeErr);
			} catch (Exception ex) {
				onSetupFailed?.Invoke(ex);
				yield break;
			}
			if (nativeOk && !string.IsNullOrEmpty(nativePath)) {
				Debug.Log("[AddonInstallFromFile_Helper] Native dialog picked: " + nativePath);
				onPickedPath?.Invoke(nativePath);
			} else if (string.Equals(nativeErr, "cancelled", StringComparison.Ordinal)) {
				Debug.Log("[AddonInstallFromFile_Helper] Native dialog cancelled.");
				onCanceled?.Invoke();
			} else {
				onSetupFailed?.Invoke(new InvalidOperationException(
					string.IsNullOrEmpty(nativeErr) ? "Native file dialog failed." : nativeErr));
			}
			yield break;
#else
			yield return CoPickWithSimpleFileBrowser(
				overlayCanvasSortOrder, onPickedPath, onCanceled, onSetupFailed, overlayCanvasToYield);
#endif
		}

		static IEnumerator CoPickWithSimpleFileBrowser(
			int overlayCanvasSortOrder,
			Action<string> onPickedPath,
			Action onCanceled,
			Action<Exception> onSetupFailed,
			Canvas overlayCanvasToYield) {
			bool finished = false;
			void DonePath(string path) {
				finished = true;
				UnparkManagerOverlay();
				EnsureAddonManagerCanvasRaycastersEnabled();
				if (!string.IsNullOrEmpty(path))
					onPickedPath?.Invoke(path);
				else
					onCanceled?.Invoke();
			}
			void DoneCancel() {
				finished = true;
				UnparkManagerOverlay();
				EnsureAddonManagerCanvasRaycastersEnabled();
				onCanceled?.Invoke();
			}
			void DoneFail(Exception ex) {
				finished = true;
				AbortInstallDialogAndRestoreUi();
				onSetupFailed?.Invoke(ex);
			}

			try {
				ParkManagerOverlay(overlayCanvasToYield);
				if (!EnsureFileBrowserInstance(out string deny)) {
					DoneFail(new InvalidOperationException(deny));
					yield break;
				}
				FileBrowser.SetFilters(true,
					new FileBrowser.Filter("Add-on (.zip)", "zip"),
					new FileBrowser.Filter("Add-on entry (__init__.py)", "py"));
				FileBrowser.SetDefaultFilter("zip");
				ElevateFileBrowserCanvas(overlayCanvasSortOrder);
				FileBrowser.ShowLoadDialog(
					paths => {
						string p = (paths != null && paths.Length > 0) ? paths[0] : null;
						DonePath(p);
					},
					DoneCancel,
					FileBrowser.PickMode.Files,
					false,
					null,
					null,
					"Install add-on (.zip or __init__.py)",
					"Install");
				ElevateFileBrowserCanvas(overlayCanvasSortOrder);
				Debug.Log($"[AddonInstallFromFile_Helper] ShowLoadDialog returned; IsOpen={FileBrowser.IsOpen}");
			} catch (Exception ex) {
				DoneFail(ex);
				yield break;
			}

			yield return null;
			if (!finished && !FileBrowser.IsOpen) {
				DoneFail(new InvalidOperationException("In-game file browser failed to open."));
				yield break;
			}

			float waited = 0f;
			while (!finished && waited < 600f) {
				waited += Time.unscaledDeltaTime;
				yield return null;
			}
			if (!finished) {
				Debug.LogWarning("[AddonInstallFromFile_Helper] File browser timed out — restoring UI.");
				AbortInstallDialogAndRestoreUi();
				onCanceled?.Invoke();
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
			fbCanvas.sortingOrder = Math.Max(overlayCanvasSortOrder + SortOrderOffsetAboveOverlay, 40000);
			if (fbCanvas.GetComponent<GraphicRaycaster>() == null)
				fbCanvas.gameObject.AddComponent<GraphicRaycaster>();
			fbCanvas.enabled = true;
			inst.gameObject.SetActive(true);
		}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		const int OFN_FILEMUSTEXIST = 0x00001000;
		const int OFN_PATHMUSTEXIST = 0x00000800;
		const int OFN_NOCHANGEDIR = 0x00000008;
		const int OFN_EXPLORER = 0x00080000;
		const int OFN_HIDEREADONLY = 0x00000004;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		struct OpenFileNameNative {
			public int structSize;
			public IntPtr dlgOwner;
			public IntPtr instance;
			public IntPtr filter;
			public IntPtr customFilter;
			public int maxCustFilter;
			public int filterIndex;
			public IntPtr file;
			public int maxFile;
			public IntPtr fileTitle;
			public int maxFileTitle;
			public IntPtr initialDir;
			public IntPtr title;
			public int flags;
			public short fileOffset;
			public short fileExtension;
			public IntPtr defExt;
			public IntPtr custData;
			public IntPtr hook;
			public IntPtr templateName;
			public IntPtr reservedPtr;
			public int reservedInt;
			public int flagsEx;
		}

		[DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
		static extern bool GetOpenFileNameW(ref OpenFileNameNative ofn);

		static IntPtr AllocDoubleNullTerminatedFilter(string[] pairs) {
			// OPENFILENAME filter is label\0pattern\0…\0\0 — raw buffer; C# string marshaling truncates at first \0.
			int chars = 1;
			for (int i = 0; i < pairs.Length; i++)
				chars += pairs[i].Length + 1;
			var buf = new char[chars];
			int o = 0;
			for (int i = 0; i < pairs.Length; i++) {
				string s = pairs[i];
				s.CopyTo(0, buf, o, s.Length);
				o += s.Length;
				buf[o++] = '\0';
			}
			buf[o] = '\0';
			IntPtr p = Marshal.AllocHGlobal(buf.Length * 2);
			Marshal.Copy(buf, 0, p, buf.Length);
			return p;
		}

		static bool TryNativeWindowsOpenZipOrInitPy(out string path, out string error) {
			path = null;
			error = null;
			IntPtr fileBuf = IntPtr.Zero;
			IntPtr fileTitleBuf = IntPtr.Zero;
			IntPtr filterBuf = IntPtr.Zero;
			IntPtr titlePtr = IntPtr.Zero;
			IntPtr defExtPtr = IntPtr.Zero;
			try {
				const int maxFile = 2048;
				fileBuf = Marshal.AllocHGlobal(maxFile * 2);
				fileTitleBuf = Marshal.AllocHGlobal(256 * 2);
				for (int i = 0; i < maxFile * 2; i++)
					Marshal.WriteByte(fileBuf, i, 0);
				for (int i = 0; i < 256 * 2; i++)
					Marshal.WriteByte(fileTitleBuf, i, 0);
				filterBuf = AllocDoubleNullTerminatedFilter(new[] {
					"Add-on zip (*.zip)", "*.zip",
					"Add-on entry (__init__.py)", "__init__.py",
					"All files (*.*)", "*.*"
				});
				titlePtr = Marshal.StringToHGlobalUni("Install add-on (.zip or __init__.py)");
				defExtPtr = Marshal.StringToHGlobalUni("zip");

				var ofn = new OpenFileNameNative();
				ofn.structSize = Marshal.SizeOf(typeof(OpenFileNameNative));
				ofn.filter = filterBuf;
				ofn.file = fileBuf;
				ofn.maxFile = maxFile;
				ofn.fileTitle = fileTitleBuf;
				ofn.maxFileTitle = 256;
				ofn.title = titlePtr;
				ofn.defExt = defExtPtr;
				ofn.filterIndex = 1;
				ofn.flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_HIDEREADONLY;

				if (!GetOpenFileNameW(ref ofn)) {
					error = "cancelled";
					return false;
				}
				path = Marshal.PtrToStringUni(fileBuf);
				if (string.IsNullOrEmpty(path)) {
					error = "cancelled";
					return false;
				}
				return true;
			} catch (Exception ex) {
				error = ex.Message;
				return false;
			} finally {
				if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
				if (fileTitleBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileTitleBuf);
				if (filterBuf != IntPtr.Zero) Marshal.FreeHGlobal(filterBuf);
				if (titlePtr != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
				if (defExtPtr != IntPtr.Zero) Marshal.FreeHGlobal(defExtPtr);
			}
		}
#endif
	}
}
