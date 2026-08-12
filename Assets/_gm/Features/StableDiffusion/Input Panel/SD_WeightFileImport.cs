using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using SimpleFileBrowser;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace spz {

	/// <summary>
	/// Browse / drag-drop import of checkpoint and VAE weights into WebUI model dirs
	/// (hook <c>sd.weight_local_load</c>).
	/// </summary>
	public static class SD_WeightFileImport {

		public enum Kind { Checkpoint, Vae }

		const string FromDiskButtonName = "From disk (local load)";

		static readonly string[] WeightExtensions = { ".safetensors", ".ckpt", ".pt", ".pth" };

		static bool _busy;
		static readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
		static SD_WeightFileImportPump _pump;

		public static bool IsBusy => _busy;

		public static bool IsWeightExtension(string path) {
			if (string.IsNullOrEmpty(path)) return false;
			string ext = Path.GetExtension(path);
			if (string.IsNullOrEmpty(ext)) return false;
			for (int i = 0; i < WeightExtensions.Length; i++) {
				if (string.Equals(ext, WeightExtensions[i], StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		public static bool AllFilesAreWeights(System.Collections.Generic.IList<string> files) {
			if (files == null || files.Count == 0) return false;
			for (int i = 0; i < files.Count; i++) {
				if (!IsWeightExtension(files[i])) return false;
			}
			return true;
		}

		public static bool TryResolveDestDir(Kind kind, out string absoluteDir, out string denyReason) {
			if (kind == Kind.Vae)
				return SD_SysInfo_MGR.TryResolveVaeModelsDir(out absoluteDir, out denyReason);
			return SD_SysInfo_MGR.TryResolveCheckpointModelsDir(out absoluteDir, out denyReason);
		}

		public static void BrowseAndImport(Kind kind) {
			EnsurePump();
			FileBrowser.SetFilters(true,
				new FileBrowser.Filter("SD weights", "safetensors", "ckpt", "pt", "pth"),
				new FileBrowser.Filter("Safetensors", "safetensors"),
				new FileBrowser.Filter("Checkpoint", "ckpt", "pt", "pth"));
			FileBrowser.SetDefaultFilter("safetensors");
			string title = kind == Kind.Vae ? "Load SD VAE from disk" : "Load SD Model from disk";
			FileBrowser.ShowLoadDialog(
				(paths) => {
					if (paths == null || paths.Length == 0) return;
					ImportFromPath(kind, paths[0]);
				},
				null,
				FileBrowser.PickMode.Files,
				false,
				null,
				null,
				title,
				"Load");
		}

		/// <summary>Import a weight file; copies into WebUI dir when needed, then prefers dropdown selection.</summary>
		public static void ImportFromPath(Kind kind, string absolutePath) {
			EnsurePump();
			if (_busy) {
				Status("Already copying a model/VAE. Please wait.", false);
				return;
			}
			if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath)) {
				Status("File not found.", false);
				return;
			}
			if (!IsWeightExtension(absolutePath)) {
				Status("Unsupported weight type (use .safetensors / .ckpt / .pt / .pth).", false);
				return;
			}
			if (!TryResolveDestDir(kind, out string destDir, out string denyReason)) {
				Status(string.IsNullOrEmpty(denyReason)
					? "WebUI DataPath empty — wait until Stable Diffusion is connected."
					: denyReason, false);
				return;
			}

			Directory.CreateDirectory(destDir);
			string fileName = Path.GetFileName(absolutePath);
			string destPath = Path.Combine(destDir, fileName).Replace('\\', '/');
			string srcFull;
			string destDirFull;
			try {
				srcFull = Path.GetFullPath(absolutePath);
				destDirFull = Path.GetFullPath(destDir);
			} catch (Exception ex) {
				Status("Invalid path: " + ex.Message, false);
				return;
			}
			string destDirPrefix = destDirFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				+ Path.DirectorySeparatorChar;
			bool alreadyInDest = srcFull.StartsWith(destDirPrefix, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(srcFull, Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase);

			if (alreadyInDest) {
				string preferName = PreferNameFromPath(kind, destDirPrefix, srcFull, fileName);
				PreferAndMaybeSelect(kind, preferName);
				Status("Selected " + PreferLabel(kind, preferName) + " (already in WebUI folder).", false);
				return;
			}

			if (File.Exists(destPath)) {
				if (ConfirmPopup_UI.instance != null) {
					ConfirmPopup_UI.instance.Show(
						"Overwrite existing file in WebUI folder?\n" + fileName,
						() => StartCopy(kind, absolutePath, destPath, fileName),
						null,
						"Overwrite",
						"Cancel");
					return;
				}
			}

			StartCopy(kind, absolutePath, destPath, fileName);
		}

		/// <summary>Ensure a From disk… button exists under the Download More slide-out vertical group.</summary>
		public static Button EnsureFromDiskButton(SlideOut_Widget_UI slideOut, Kind kind, Action onClick) {
			if (slideOut == null) return null;
			Transform vertical = FindVerticalGroup(slideOut.transform);
			if (vertical == null) vertical = slideOut.transform;

			Transform existing = vertical.Find(FromDiskButtonName);
			Button btn = null;
			if (existing != null)
				btn = existing.GetComponent<Button>();

			if (btn == null) {
				Button template = vertical.GetComponentInChildren<Button>(true);
				GameObject go;
				if (template != null) {
					go = UnityEngine.Object.Instantiate(template.gameObject, vertical);
					go.name = FromDiskButtonName;
					// Strip OpenURL / download helpers so we only fire our listener.
					foreach (var open in go.GetComponents<OpenURL_and_Subdirectory>()) {
						open.enabled = false;
						UnityEngine.Object.DestroyImmediate(open);
					}
					// Cloned civitai rows carry download-site tooltips — remove so From disk is not mislabeled.
					foreach (var tip in go.GetComponents<CanShowTooltip_UI>())
						UnityEngine.Object.DestroyImmediate(tip);
					btn = go.GetComponent<Button>();
					btn.onClick.RemoveAllListeners();
					var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
					if (label != null)
						label.text = "From disk…";
				} else {
					go = new GameObject(FromDiskButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
					go.transform.SetParent(vertical, false);
					var le = go.GetComponent<LayoutElement>();
					le.minHeight = 24f;
					le.preferredHeight = 24f;
					le.flexibleWidth = 1f;
					var img = go.GetComponent<Image>();
					img.color = new Color(0.85f, 0.85f, 0.9f, 1f);
					btn = go.GetComponent<Button>();
					btn.targetGraphic = img;
					var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
					textGo.transform.SetParent(go.transform, false);
					var rt = textGo.GetComponent<RectTransform>();
					rt.anchorMin = Vector2.zero;
					rt.anchorMax = Vector2.one;
					rt.offsetMin = new Vector2(4, 0);
					rt.offsetMax = new Vector2(-4, 0);
					var tmp = textGo.GetComponent<TextMeshProUGUI>();
					tmp.text = "From disk…";
					tmp.fontSize = 14f;
					tmp.alignment = TextAlignmentOptions.Center;
					tmp.raycastTarget = false;
				}
				go.transform.SetAsFirstSibling();
			}

			if (btn != null) {
				btn.onClick.RemoveAllListeners();
				btn.onClick.AddListener(() => onClick?.Invoke());
				btn.interactable = true;
			}

			// Download More VAE slide is short; grow so From disk + civitai both fit.
			var slideRt = slideOut.transform as RectTransform;
			if (slideRt != null && slideRt.sizeDelta.y < 72f)
				slideRt.sizeDelta = new Vector2(slideRt.sizeDelta.x, 72f);

			return btn;
		}

		static Transform FindVerticalGroup(Transform root) {
			if (root == null) return null;
			var layouts = root.GetComponentsInChildren<UnityEngine.UI.VerticalLayoutGroup>(true);
			if (layouts != null && layouts.Length > 0 && layouts[0] != null)
				return layouts[0].transform;
			foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
				if (t != null && t.name != null && t.name.IndexOf("vertical", StringComparison.OrdinalIgnoreCase) >= 0)
					return t;
			}
			return null;
		}

		static void StartCopy(Kind kind, string sourcePath, string destPath, string fileName) {
			_busy = true;
			Status("Copying " + fileName + " into WebUI models…", true);
			Task.Run(() => {
				string err = null;
				try {
					Directory.CreateDirectory(Path.GetDirectoryName(destPath));
					File.Copy(sourcePath, destPath, overwrite: true);
				} catch (Exception ex) {
					err = ex.Message;
				}
				_mainThread.Enqueue(() => {
					_busy = false;
					if (!string.IsNullOrEmpty(err)) {
						Status("Copy failed: " + err, false);
						return;
					}
					PreferAndMaybeSelect(kind, fileName);
					Status("Copied " + fileName + ". Waiting for WebUI list refresh…", false);
				});
			});
		}

		static void PreferAndMaybeSelect(Kind kind, string fileNameOrRelative) {
			if (kind == Kind.Vae) {
				SD_VAE.instance?.PreferVAEWhenAvailable(fileNameOrRelative);
			} else {
				// Checkpoint dropdown uses stems; keep subdir/stem when present.
				string prefer = fileNameOrRelative;
				if (prefer.IndexOf('/') >= 0 || prefer.IndexOf('\\') >= 0) {
					string dir = Path.GetDirectoryName(prefer) ?? "";
					string stem = Path.GetFileNameWithoutExtension(prefer);
					prefer = string.IsNullOrEmpty(dir) ? stem : Path.Combine(dir, stem).Replace('\\', '/');
				} else {
					prefer = Path.GetFileNameWithoutExtension(prefer);
				}
				SD_Neural_Models.instance?.PreferModelWhenAvailable(prefer);
			}
		}

		/// <summary>WebUI titles for nested files are relative paths (subdir/name), not basenames.</summary>
		static string PreferNameFromPath(Kind kind, string destDirPrefix, string srcFull, string fileName) {
			if (srcFull.StartsWith(destDirPrefix, StringComparison.OrdinalIgnoreCase)) {
				string rel = srcFull.Substring(destDirPrefix.Length).Replace('\\', '/');
				if (!string.IsNullOrEmpty(rel))
					return rel;
			}
			return fileName;
		}

		static string PreferLabel(Kind kind, string fileName) {
			if (kind == Kind.Vae) return Path.GetFileName(fileName);
			if (fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0) {
				string dir = Path.GetDirectoryName(fileName) ?? "";
				string stem = Path.GetFileNameWithoutExtension(fileName);
				return string.IsNullOrEmpty(dir) ? stem : (dir.Replace('\\', '/') + "/" + stem);
			}
			return Path.GetFileNameWithoutExtension(fileName);
		}

		static void Status(string msg, bool showProgress) {
			if (Viewport_StatusText.instance == null) {
				Debug.Log("[SD_WeightFileImport] " + msg);
				return;
			}
			Viewport_StatusText.instance.ShowStatusText(msg, false, showProgress ? 12f : 4f, showProgress);
			if (!showProgress)
				Viewport_StatusText.instance.ReportProgress(1f);
		}

		static void EnsurePump() {
			// Unity destroyed objects compare equal to null via overloaded ==.
			if (_pump != null) return;
			var go = new GameObject("SD_WeightFileImportPump");
			UnityEngine.Object.DontDestroyOnLoad(go);
			_pump = go.AddComponent<SD_WeightFileImportPump>();
		}

		internal static void PumpMainThread() {
			while (_mainThread.TryDequeue(out Action a)) {
				try { a?.Invoke(); }
				catch (Exception ex) { Debug.LogException(ex); }
			}
		}

		sealed class SD_WeightFileImportPump : MonoBehaviour {
			void Update() => PumpMainThread();
			void OnDestroy() {
				if (_pump == this)
					_pump = null;
			}
		}
	}
}
