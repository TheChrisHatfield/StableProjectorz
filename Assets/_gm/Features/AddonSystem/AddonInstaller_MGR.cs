using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Handles installation, removal, and management of add-on zip files.
	/// Similar to Blender's add-on installer system.
	/// </summary>
	public class AddonInstaller_MGR : MonoBehaviour {
		public static AddonInstaller_MGR instance { get; private set; }
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
		
		/// <summary>
		/// Installs an add-on from a zip file.
		/// Extracts the zip to StreamingAssets/Addons/ and validates it has __init__.py
		/// </summary>
		/// <param name="zipFilePath">Path to the zip file</param>
		/// <param name="onComplete">Callback with (success, message, addonId)</param>
		public void InstallAddonFromZip(string zipFilePath, Action<bool, string, string> onComplete) {
			if (!File.Exists(zipFilePath)) {
				onComplete?.Invoke(false, "Zip file not found", null);
				return;
			}
			
			string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
			if (!Directory.Exists(addonsPath)) {
				Directory.CreateDirectory(addonsPath);
			}
			
			StartCoroutine(InstallAddonCoroutine(zipFilePath, addonsPath, onComplete));
		}
		
		IEnumerator InstallAddonCoroutine(string zipFilePath, string addonsPath, Action<bool, string, string> onComplete) {
			string tempExtractPath = null;
			string addonId = null;
			
			try {
				// Create temporary extraction directory
				tempExtractPath = Path.Combine(Path.GetTempPath(), $"spz_addon_{Guid.NewGuid()}");
				Directory.CreateDirectory(tempExtractPath);
				
				// Extract zip to temp directory
				ZipFile.ExtractToDirectory(zipFilePath, tempExtractPath, true);
				
				// Find the add-on directory (could be root of zip or in a subdirectory)
				string addonRoot = FindAddonRoot(tempExtractPath);
				
				if (addonRoot == null) {
					onComplete?.Invoke(false, "No __init__.py found in zip file", null);
					yield break;
				}
				
				// Get add-on ID from directory name or __init__.py metadata
				addonId = GetAddonId(addonRoot);
				
				if (string.IsNullOrEmpty(addonId)) {
					addonId = Path.GetFileName(addonRoot);
					if (string.IsNullOrEmpty(addonId)) {
						addonId = $"Addon_{DateTime.Now:yyyyMMdd_HHmmss}";
					}
				}
				
				// Check if add-on already exists
				string targetPath = Path.Combine(addonsPath, addonId);
				if (Directory.Exists(targetPath)) {
					// Ask user if they want to overwrite (for now, we'll create a backup)
					string backupPath = $"{targetPath}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
					Directory.Move(targetPath, backupPath);
					UnityEngine.Debug.Log($"[AddonInstaller] Backed up existing add-on to {backupPath}");
				}
				
				// Copy to final location
				CopyDirectory(addonRoot, targetPath);
				
				// Verify installation
				string initFile = Path.Combine(targetPath, "__init__.py");
				if (!File.Exists(initFile)) {
					onComplete?.Invoke(false, "Installation failed: __init__.py not found after extraction", null);
					yield break;
				}
				
				// Trigger add-on discovery
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.DiscoverAddons();
				}
				
				onComplete?.Invoke(true, $"Add-on '{addonId}' installed successfully", addonId);
				
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Error installing add-on: {e.Message}");
				onComplete?.Invoke(false, $"Installation failed: {e.Message}", null);
			} finally {
				// Clean up temp directory
				if (tempExtractPath != null && Directory.Exists(tempExtractPath)) {
					try {
						Directory.Delete(tempExtractPath, true);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not delete temp directory: {e.Message}");
					}
				}
			}
		}
		
		/// <summary>
		/// Finds the root directory containing __init__.py
		/// Handles cases where zip contains the add-on directly or in a subdirectory
		/// </summary>
		string FindAddonRoot(string extractPath) {
			// Check root
			if (File.Exists(Path.Combine(extractPath, "__init__.py"))) {
				return extractPath;
			}
			
			// Check subdirectories (common case: zip contains a folder with the add-on name)
			var subdirs = Directory.GetDirectories(extractPath);
			foreach (var subdir in subdirs) {
				if (File.Exists(Path.Combine(subdir, "__init__.py"))) {
					return subdir;
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Tries to get add-on ID from __init__.py metadata (bl_info style)
		/// Falls back to directory name if not found
		/// </summary>
		string GetAddonId(string addonRoot) {
			string initFile = Path.Combine(addonRoot, "__init__.py");
			if (!File.Exists(initFile)) return null;
			
			try {
				string[] lines = File.ReadAllLines(initFile);
				foreach (string line in lines) {
					// Look for common patterns like "bl_info" or "addon_id" or "__name__"
					if (line.Contains("__name__") || line.Contains("addon_id") || line.Contains("id")) {
						// Simple extraction - could be improved with regex
						int eqIndex = line.IndexOf('=');
						if (eqIndex > 0) {
							string value = line.Substring(eqIndex + 1).Trim().Trim('"', '\'', ' ');
							if (!string.IsNullOrEmpty(value)) {
								return value;
							}
						}
					}
				}
			} catch {
				// If we can't read the file, just use directory name
			}
			
			return null;
		}
		
		/// <summary>
		/// Copies a directory recursively
		/// </summary>
		void CopyDirectory(string sourceDir, string destDir) {
			Directory.CreateDirectory(destDir);
			
			foreach (string file in Directory.GetFiles(sourceDir)) {
				string fileName = Path.GetFileName(file);
				string destFile = Path.Combine(destDir, fileName);
				File.Copy(file, destFile, true);
			}
			
			foreach (string subdir in Directory.GetDirectories(sourceDir)) {
				string dirName = Path.GetFileName(subdir);
				string destSubdir = Path.Combine(destDir, dirName);
				CopyDirectory(subdir, destSubdir);
			}
		}
		
		/// <summary>
		/// Removes an add-on by ID
		/// </summary>
		public void RemoveAddon(string addonId, Action<bool, string> onComplete) {
			if (string.IsNullOrEmpty(addonId)) {
				onComplete?.Invoke(false, "Invalid add-on ID");
				return;
			}
			
			string addonPath = Path.Combine(Application.streamingAssetsPath, "Addons", addonId);
			
			if (!Directory.Exists(addonPath)) {
				onComplete?.Invoke(false, $"Add-on '{addonId}' not found");
				return;
			}
			
			try {
				// Unload add-on first if it's loaded
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.UnloadAddon(addonId);
				}
				
				// Delete directory
				Directory.Delete(addonPath, true);
				
				// Refresh discovery
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.DiscoverAddons();
				}
				
				onComplete?.Invoke(true, $"Add-on '{addonId}' removed successfully");
				
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Error removing add-on: {e.Message}");
				onComplete?.Invoke(false, $"Removal failed: {e.Message}");
			}
		}
	}
}
