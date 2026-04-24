using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Editor: drop a <c>.zip</c> add-on package onto the Scene view (or use the menu) to install into <c>StreamingAssets/Addons</c>.
	/// </summary>
	[InitializeOnLoad]
	static class AddonZipSceneViewInstallHook {

		static AddonZipSceneViewInstallHook() {
			SceneView.duringSceneGui -= OnDuringSceneGui;
			SceneView.duringSceneGui += OnDuringSceneGui;
		}

		/// <summary>
		/// Avoid handling the same OS drag in every open Scene tab (duringSceneGui runs per SceneView).
		/// </summary>
		static bool IsThisSceneViewTheOsDragTarget(SceneView sceneView) {
			var mw = EditorWindow.mouseOverWindow;
			if (mw == sceneView) {
				return true;
			}
			// mouseOverWindow can be null briefly; single consumer via last active Scene view.
			if (mw == null && SceneView.lastActiveSceneView == sceneView) {
				return true;
			}
			return false;
		}

		static void OnDuringSceneGui(SceneView sceneView) {
			Event e = Event.current;
			if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) {
				return;
			}
			if (!TryGetFirstZipPathFromDrag(out string zipPath)) {
				return;
			}
			if (!IsThisSceneViewTheOsDragTarget(sceneView)) {
				return;
			}
			DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
			if (e.type == EventType.DragPerform) {
				DragAndDrop.AcceptDrag();
				TryInstallFromEditor(zipPath);
				e.Use();
			}
		}

		static bool TryGetFirstZipPathFromDrag(out string zipPath) {
			zipPath = null;
			if (DragAndDrop.paths == null || DragAndDrop.paths.Length == 0) {
				return false;
			}
			foreach (string p in DragAndDrop.paths) {
				if (string.IsNullOrEmpty(p)) {
					continue;
				}
				if (p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p)) {
					zipPath = p;
					return true;
				}
			}
			return false;
		}

		[MenuItem("Stable Projectorz/Add-ons/Install Add-on from Zip…")]
		static void MenuInstallAddonZip() {
			string path = EditorUtility.OpenFilePanel("Install add-on (.zip)", "", "zip");
			if (string.IsNullOrEmpty(path)) {
				return;
			}
			TryInstallFromEditor(path);
		}

		static void TryInstallFromEditor(string zipPath) {
			zipPath = AddonInstaller_MGR.NormalizeZipPathForInstall(zipPath);
			if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath)) {
				EditorUtility.DisplayDialog("Add-on install", "Zip file not found or path is invalid.", "OK");
				return;
			}
			string streamingRoot = Application.streamingAssetsPath;
			if (string.IsNullOrEmpty(streamingRoot)) {
				EditorUtility.DisplayDialog("Add-on install", "StreamingAssets path is not available for this project.", "OK");
				return;
			}
			string addonsPath = Path.Combine(streamingRoot, "Addons");
			string tempExtractPath = Path.Combine(Path.GetTempPath(), $"spz_editor_addon_{Guid.NewGuid():N}");
			try {
				Directory.CreateDirectory(tempExtractPath);
				AddonInstaller_MGR.ExtractZipToDirectorySafe(zipPath, tempExtractPath);
			} catch (Exception ex) {
				EditorUtility.DisplayDialog("Add-on install", "Could not extract zip:\n" + ex.Message, "OK");
				SafeDeleteDir(tempExtractPath);
				return;
			}
			string addonRoot = AddonInstaller_MGR.FindAddonRootInExtractedDirectory(tempExtractPath);
			if (string.IsNullOrEmpty(addonRoot)) {
				EditorUtility.DisplayDialog("Add-on install", "No __init__.py found in this zip (not a valid add-on package).", "OK");
				SafeDeleteDir(tempExtractPath);
				return;
			}
			if (!AddonInstaller_MGR.TryPublishAddonRootToStreamingAssets(addonRoot, addonsPath, out string addonId, out string err)) {
				EditorUtility.DisplayDialog("Add-on install", err ?? "Install failed.", "OK");
				SafeDeleteDir(tempExtractPath);
				return;
			}
			SafeDeleteDir(tempExtractPath);
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Add-on install", $"Installed add-on '{addonId}' to StreamingAssets/Addons.", "OK");
			Debug.Log($"[AddonZipSceneViewInstallHook] Installed '{addonId}' from '{zipPath}'.");
		}

		static void SafeDeleteDir(string dir) {
			if (string.IsNullOrEmpty(dir)) {
				return;
			}
			try {
				if (Directory.Exists(dir)) {
					Directory.Delete(dir, true);
				}
			} catch (Exception e) {
				Debug.LogWarning($"[AddonZipSceneViewInstallHook] Temp cleanup failed: {e.Message}");
			}
		}
	}
}
