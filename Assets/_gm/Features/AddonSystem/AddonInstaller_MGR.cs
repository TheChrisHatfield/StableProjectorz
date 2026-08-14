using System;
using System.Collections;
using System.Collections.Generic;
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
		readonly HashSet<string> _removeInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public bool IsRemoveInFlight(string addonId) =>
			!string.IsNullOrEmpty(addonId) && _removeInFlight.Contains(addonId);
		
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
			if (string.IsNullOrEmpty(zipFilePath)) {
				onComplete?.Invoke(false, "Invalid zip file path", null);
				return;
			}
			
			bool fileExists = false;
			try {
				fileExists = File.Exists(zipFilePath);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to check if zip file exists: {e.Message}");
				onComplete?.Invoke(false, $"Failed to check zip file: {e.Message}", null);
				return;
			}
			
			if (!fileExists) {
				onComplete?.Invoke(false, "Zip file not found", null);
				return;
			}
			
			// Check if streamingAssetsPath is valid first
			if (string.IsNullOrEmpty(Application.streamingAssetsPath)) {
				UnityEngine.Debug.LogError("[AddonInstaller] Application.streamingAssetsPath is null or empty");
				onComplete?.Invoke(false, "StreamingAssets path is not available", null);
				return;
			}
			
			string addonsPath = null;
			try {
				addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to combine paths: {e.Message}");
				onComplete?.Invoke(false, $"Failed to construct addons path: {e.Message}", null);
				return;
			}
			
			if (string.IsNullOrEmpty(addonsPath)) {
				onComplete?.Invoke(false, "Addons path is null or empty", null);
				return;
			}
			
			bool dirExists = false;
			try {
				dirExists = Directory.Exists(addonsPath);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to check if addons directory exists: {e.Message}");
				onComplete?.Invoke(false, $"Failed to check addons directory: {e.Message}", null);
				return;
			}
			
			if (!dirExists) {
				try {
					Directory.CreateDirectory(addonsPath);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to create addons directory: {e.Message}");
					onComplete?.Invoke(false, $"Failed to create addons directory: {e.Message}", null);
					return;
				}
			}
			
			StartCoroutine(InstallAddonCoroutine(zipFilePath, addonsPath, onComplete));
		}

		/// <summary>
		/// Install from an on-disk add-on folder (or its <c>__init__.py</c>). Unloads overwrite targets
		/// the same way zip install does before moving StreamingAssets.
		/// </summary>
		public void InstallAddonFromFolder(string addonRootOrInitPy, Action<bool, string, string> onComplete) {
			StartCoroutine(InstallAddonFromFolderCrtn(addonRootOrInitPy, onComplete));
		}

		IEnumerator InstallAddonFromFolderCrtn(string addonRootOrInitPy, Action<bool, string, string> onComplete) {
			string root = addonRootOrInitPy;
			if (!string.IsNullOrEmpty(root) && File.Exists(root)
			    && Path.GetExtension(root).Equals(".py", StringComparison.OrdinalIgnoreCase)) {
				root = Path.GetDirectoryName(root);
			}
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
				onComplete?.Invoke(false, "Could not resolve add-on folder", null);
				yield break;
			}

			string addonId = GetAddonIdFromRoot(root);
			if (string.IsNullOrEmpty(addonId)) {
				try {
					addonId = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
				} catch { /* fall through */ }
			}

			string addonsPathEarly = null;
			try {
				addonsPathEarly = Path.Combine(Application.streamingAssetsPath, "Addons");
			} catch { /* fall through to normal install */ }
			if (!string.IsNullOrEmpty(addonsPathEarly) && !string.IsNullOrEmpty(addonId) && !string.IsNullOrEmpty(root)) {
				try {
					string rootFull = Path.GetFullPath(root);
					string targetFull = Path.GetFullPath(Path.Combine(addonsPathEarly, addonId));
					if (string.Equals(rootFull, targetFull, StringComparison.OrdinalIgnoreCase)) {
						// Picking StreamingAssets/Addons/<id> (or its __init__.py) would Move the folder
						// then fail to copy from the moved path — leave files alone.
						onComplete?.Invoke(true, $"Add-on '{addonId}' is already installed", addonId);
						yield break;
					}
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[AddonInstaller] Self-path check failed (continuing install): {e.Message}");
				}
			}

			bool wasEnabledBeforeOverwrite = false;
			if (Addon_MGR.instance != null && !string.IsNullOrEmpty(addonId)) {
				var registered = Addon_MGR.instance.GetAddons();
				bool wasRegistered = registered != null && registered.ContainsKey(addonId);
				wasEnabledBeforeOverwrite = wasRegistered && Addon_MGR.instance.IsAddonEnabled(addonId);
				if (wasRegistered) {
					bool unloadDone = false;
					Addon_MGR.instance.UnloadAddon(addonId, () => unloadDone = true);
					float waitUnload = 0f;
					const float unloadTimeoutSec = 45f;
					while (!unloadDone && waitUnload < unloadTimeoutSec) {
						waitUnload += Time.unscaledDeltaTime;
						yield return null;
					}
					if (!unloadDone) {
						UnityEngine.Debug.LogWarning(
							$"[AddonInstaller] Unity unload for '{addonId}' timed out during folder install — proceeding with overwrite.");
					}
					float waitPending = 0f;
					const float pendingTimeoutSec = 45f;
					while (Addon_MGR.instance != null
					       && Addon_MGR.instance.IsPythonUnloadPending(addonId)
					       && waitPending < pendingTimeoutSec) {
						waitPending += Time.unscaledDeltaTime;
						yield return null;
					}
					if (Addon_MGR.instance != null && Addon_MGR.instance.IsPythonUnloadPending(addonId)) {
						UnityEngine.Debug.LogWarning(
							$"[AddonInstaller] Python unload for '{addonId}' still pending during folder install — proceeding with overwrite.");
					}
				}
			}

			string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
			if (!TryPublishAddonRootToStreamingAssets(root, addonsPath, out string publishedId, out string err)) {
				onComplete?.Invoke(false, err ?? "Folder install failed", null);
				yield break;
			}
			if (wasEnabledBeforeOverwrite && Addon_MGR.instance != null && !string.IsNullOrEmpty(publishedId))
				Addon_MGR.instance.EnableAddon(publishedId);
			onComplete?.Invoke(true, $"Add-on '{publishedId}' installed successfully", publishedId);
		}

		IEnumerator InstallAddonCoroutine(string zipFilePath, string addonsPath, Action<bool, string, string> onComplete) {
			string tempExtractPath = null;
			string targetPath = null; // Track for cleanup on partial install failure
			string backupPath = null; // If we moved existing addon here, restore on failure
			string addonId = null;
			string addonRoot = null;
			bool installSucceeded = false;
			
			try {
				// Create temporary extraction directory
				string tempPath = null;
				try {
					tempPath = Path.GetTempPath();
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to get temp path: {e.Message}");
					onComplete?.Invoke(false, $"Failed to get temp path: {e.Message}", null);
					yield break;
				}
				
				if (string.IsNullOrEmpty(tempPath)) {
					onComplete?.Invoke(false, "Temp path is null or empty", null);
					yield break;
				}
				
				try {
					tempExtractPath = Path.Combine(tempPath, $"spz_addon_{Guid.NewGuid()}");
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to combine temp paths: {e.Message}");
					onComplete?.Invoke(false, $"Failed to construct temp extract path: {e.Message}", null);
					yield break;
				}
				
				if (string.IsNullOrEmpty(tempExtractPath)) {
					onComplete?.Invoke(false, "Temp extract path is null or empty", null);
					yield break;
				}
				
				try {
					Directory.CreateDirectory(tempExtractPath);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to create temp directory: {e.Message}");
					onComplete?.Invoke(false, $"Failed to create temp directory: {e.Message}", null);
					yield break;
				}
				
				// Extract zip to temp directory
				try {
					ZipFile.ExtractToDirectory(zipFilePath, tempExtractPath, true);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to extract zip file: {e.Message}");
					onComplete?.Invoke(false, $"Failed to extract zip file: {e.Message}", null);
					yield break;
				}
				
				// Find the add-on directory (could be root of zip or in a subdirectory)
				addonRoot = FindAddonRootInExtractedDirectory(tempExtractPath);
				
				if (addonRoot == null) {
					onComplete?.Invoke(false, "No __init__.py found in zip file", null);
					yield break;
				}
				
				// Get add-on ID from directory name or __init__.py metadata
				addonId = GetAddonIdFromRoot(addonRoot);
				
				if (string.IsNullOrEmpty(addonId)) {
					try {
						addonId = Path.GetFileName(addonRoot);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to get filename from addon root: {e.Message}");
					}
					
					if (string.IsNullOrEmpty(addonId)) {
						addonId = $"Addon_{DateTime.Now:yyyyMMdd_HHmmss}";
					}
				}
				
				// Check if add-on already exists
				try {
					targetPath = Path.Combine(addonsPath, addonId);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to combine target path: {e.Message}");
					onComplete?.Invoke(false, $"Failed to construct target path: {e.Message}", null);
					yield break;
				}
				
				if (string.IsNullOrEmpty(targetPath)) {
					onComplete?.Invoke(false, "Target path is null or empty", null);
					yield break;
				}
				
				bool targetExists = false;
				try {
					targetExists = Directory.Exists(targetPath);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to check if target directory exists: {e.Message}");
					onComplete?.Invoke(false, $"Cannot install: failed to check if addon already exists ({e.Message}). Refusing to overwrite without backup.", null);
					yield break;
				}

			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Error preparing add-on install: {e.Message}");
				onComplete?.Invoke(false, $"Installation failed: {e.Message}", null);
				yield break;
			}

			// Yield must stay outside try/catch (CS1626). Unload before replacing on-disk files.
			bool wasEnabledBeforeOverwrite = false;
			bool targetExistsForOverwrite = false;
			try {
				targetExistsForOverwrite = !string.IsNullOrEmpty(targetPath) && Directory.Exists(targetPath);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to re-check target directory: {e.Message}");
				onComplete?.Invoke(false, $"Cannot install: failed to check if addon already exists ({e.Message}).", null);
				yield break;
			}
			if (targetExistsForOverwrite && Addon_MGR.instance != null && !string.IsNullOrEmpty(addonId)) {
				var registered = Addon_MGR.instance.GetAddons();
				bool wasRegistered = registered != null && registered.ContainsKey(addonId);
				wasEnabledBeforeOverwrite = wasRegistered && Addon_MGR.instance.IsAddonEnabled(addonId);
				if (wasRegistered) {
					bool unloadDone = false;
					Addon_MGR.instance.UnloadAddon(addonId, () => unloadDone = true);
					float waitUnload = 0f;
					const float unloadTimeoutSec = 45f;
					while (!unloadDone && waitUnload < unloadTimeoutSec) {
						waitUnload += Time.unscaledDeltaTime;
						yield return null;
					}
					if (!unloadDone) {
						UnityEngine.Debug.LogWarning(
							$"[AddonInstaller] Unity unload for '{addonId}' timed out during zip install — proceeding with overwrite.");
					}
					// UnloadAddon may only queue HTTP unregister — wait like RemoveAddon before moving files.
					float waitPending = 0f;
					const float pendingTimeoutSec = 45f;
					while (Addon_MGR.instance != null
					       && Addon_MGR.instance.IsPythonUnloadPending(addonId)
					       && waitPending < pendingTimeoutSec) {
						waitPending += Time.unscaledDeltaTime;
						yield return null;
					}
					if (Addon_MGR.instance != null && Addon_MGR.instance.IsPythonUnloadPending(addonId)) {
						UnityEngine.Debug.LogWarning(
							$"[AddonInstaller] Python unload for '{addonId}' still pending during zip install — proceeding with overwrite.");
					}
				}
			}

			try {
				bool targetExists = targetExistsForOverwrite;
				
				if (targetExists) {
					// Ask user if they want to overwrite (for now, we'll create a backup)
					backupPath = $"{targetPath}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
					try {
						Directory.Move(targetPath, backupPath);
						UnityEngine.Debug.Log($"[AddonInstaller] Backed up existing add-on to {backupPath}");
					} catch (Exception e) {
						UnityEngine.Debug.LogError($"[AddonInstaller] Failed to backup existing add-on: {e.Message}");
						onComplete?.Invoke(false, $"Failed to backup existing add-on: {e.Message}", null);
						yield break;
					}
				}
				
				// Copy to final location
				try {
					CopyDirectoryRecursive(addonRoot, targetPath);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to copy directory: {e.Message}");
					onComplete?.Invoke(false, $"Failed to copy add-on files: {e.Message}", null);
					yield break;
				}
				
				// Verify installation
				string initFile = null;
				try {
					initFile = Path.Combine(targetPath, "__init__.py");
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to combine init file path: {e.Message}");
					onComplete?.Invoke(false, $"Failed to construct init file path: {e.Message}", null);
					yield break;
				}
				
				bool initExists = false;
				try {
					initExists = File.Exists(initFile);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to check if init file exists: {e.Message}");
					onComplete?.Invoke(false, $"Failed to verify installation: {e.Message}", null);
					yield break;
				}
				
				if (!initExists) {
					onComplete?.Invoke(false, "Installation failed: __init__.py not found after extraction", null);
					yield break;
				}
				
				// Trigger add-on discovery
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.DiscoverAddons();
					if (wasEnabledBeforeOverwrite && !string.IsNullOrEmpty(addonId))
						Addon_MGR.instance.EnableAddon(addonId);
				}
				
				installSucceeded = true;
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
				// Clean up partially-installed target directory on failure (e.g. CopyDirectory threw)
				if (!installSucceeded && targetPath != null && Directory.Exists(targetPath)) {
					try {
						Directory.Delete(targetPath, true);
						UnityEngine.Debug.Log($"[AddonInstaller] Removed partial install at {targetPath}");
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not remove partial install directory: {e.Message}");
					}
				}
				// Restore original addon from backup if we had moved it and install failed
				if (!installSucceeded && backupPath != null && targetPath != null && Directory.Exists(backupPath)) {
					// If partial targetPath still exists (delete above failed), try once more so Move can succeed
					if (Directory.Exists(targetPath)) {
						try {
							Directory.Delete(targetPath, true);
						} catch (Exception e) {
							UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not remove partial install before restore: {e.Message}");
						}
					}
					if (!Directory.Exists(targetPath)) {
						try {
							Directory.Move(backupPath, targetPath);
							UnityEngine.Debug.Log($"[AddonInstaller] Restored original addon from backup to {targetPath}");
						} catch (Exception e) {
							UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not restore addon from backup: {e.Message}. Original remains at {backupPath}");
						}
					} else {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not restore addon: target path still in use. Original remains at {backupPath}");
					}
				}
			}
		}
		
		/// <summary>
		/// Finds the root directory containing __init__.py (zip root or single subfolder).
		/// Used by runtime installer and editor zip hook.
		/// </summary>
		public static string FindAddonRootInExtractedDirectory(string extractPath) {
			if (string.IsNullOrEmpty(extractPath)) {
				return null;
			}
			
			// Check root
			string rootInitFile = null;
			try {
				rootInitFile = Path.Combine(extractPath, "__init__.py");
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to combine root init file path: {e.Message}");
				return null;
			}
			
			bool rootInitExists = false;
			try {
				rootInitExists = File.Exists(rootInitFile);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to check root init file: {e.Message}");
			}
			
			if (rootInitExists) {
				return extractPath;
			}
			
			// Check subdirectories (common case: zip contains a folder with the add-on name)
			string[] subdirs = null;
			try {
				subdirs = Directory.GetDirectories(extractPath);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to get subdirectories: {e.Message}");
				return null;
			}
			
			if (subdirs == null) {
				return null;
			}
			
			foreach (var subdir in subdirs) {
				string subdirInitFile = null;
				try {
					subdirInitFile = Path.Combine(subdir, "__init__.py");
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to combine subdir init file path: {e.Message}");
					continue;
				}
				
				bool subdirInitExists = false;
				try {
					subdirInitExists = File.Exists(subdirInitFile);
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to check subdir init file: {e.Message}");
					continue;
				}
				
				if (subdirInitExists) {
					return subdir;
				}
			}
			
			return null;
		}
		
		/// <summary>
		/// Tries to get add-on ID from __init__.py metadata (bl_info style)
		/// Falls back to directory name if not found
		/// </summary>
		public static string GetAddonIdFromRoot(string addonRoot) {
			if (string.IsNullOrEmpty(addonRoot)) {
				return null;
			}
			
			string initFile = null;
			try {
				initFile = Path.Combine(addonRoot, "__init__.py");
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to combine init file path in GetAddonIdFromRoot: {e.Message}");
				return null;
			}
			
			if (string.IsNullOrEmpty(initFile)) {
				return null;
			}
			
			bool initExists = false;
			try {
				initExists = File.Exists(initFile);
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to check init file in GetAddonIdFromRoot: {e.Message}");
				return null;
			}
			
			if (!initExists) return null;
			
			try {
				string[] lines = File.ReadAllLines(initFile);
				foreach (string line in lines) {
					if (string.IsNullOrWhiteSpace(line)) continue;
					string trimmed = line.Trim();
					if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
					// Only explicit id assignments — never match any line containing "id" (e.g. mesh_id).
					if (!TryParseExplicitAddonIdAssignment(trimmed, out string parsedId))
						continue;
					if (IsPlausibleAddonFolderId(parsedId))
						return parsedId;
				}
			} catch {
				// If we can't read the file, just use directory name
			}
			
			return null;
		}

		/// <summary>
		/// Parses <c>ADDON_ID = "…"</c> / <c>addon_id = '…'</c> (optional leading <c>bl_info</c>-style dict keys not used).
		/// </summary>
		static bool TryParseExplicitAddonIdAssignment(string trimmedLine, out string addonId) {
			addonId = null;
			if (string.IsNullOrEmpty(trimmedLine)) return false;
			const StringComparison ord = StringComparison.Ordinal;
			string[] keys = { "ADDON_ID", "addon_id" };
			foreach (string key in keys) {
				if (!trimmedLine.StartsWith(key, ord)) continue;
				int i = key.Length;
				while (i < trimmedLine.Length && char.IsWhiteSpace(trimmedLine[i])) i++;
				if (i >= trimmedLine.Length || trimmedLine[i] != '=') continue;
				i++;
				while (i < trimmedLine.Length && char.IsWhiteSpace(trimmedLine[i])) i++;
				if (i >= trimmedLine.Length) continue;
				char q = trimmedLine[i];
				if (q != '"' && q != '\'') continue;
				int end = trimmedLine.IndexOf(q, i + 1);
				if (end <= i + 1) continue;
				addonId = trimmedLine.Substring(i + 1, end - i - 1).Trim();
				return !string.IsNullOrEmpty(addonId);
			}
			return false;
		}

		static bool IsPlausibleAddonFolderId(string id) {
			if (string.IsNullOrWhiteSpace(id)) return false;
			id = id.Trim();
			if (id.Length < 2 || id.Length > 128) return false;
			if (id.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', ' ', '(', ')' }) >= 0)
				return false;
			if (id.IndexOf('.') >= 0) return false; // reject api.models.get_pos(...) style leftovers
			for (int i = 0; i < id.Length; i++) {
				char c = id[i];
				if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
					return false;
			}
			return true;
		}
		
		/// <summary>
		/// Copies a directory recursively
		/// </summary>
		public static void CopyDirectoryRecursive(string sourceDir, string destDir) {
			if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(destDir)) {
				throw new ArgumentException("Source or destination directory is null or empty");
			}
			
			try {
				Directory.CreateDirectory(destDir);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to create destination directory: {e.Message}");
				throw;
			}
			
			string[] files = null;
			try {
				files = Directory.GetFiles(sourceDir);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to get files from source directory: {e.Message}");
				throw;
			}
			
			if (files != null) {
				foreach (string file in files) {
					string fileName = null;
					try {
						fileName = Path.GetFileName(file);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to get filename: {e.Message}");
						continue;
					}
					
					if (string.IsNullOrEmpty(fileName)) {
						continue;
					}
					
					string destFile = null;
					try {
						destFile = Path.Combine(destDir, fileName);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to combine dest file path: {e.Message}");
						continue;
					}
					
					try {
						File.Copy(file, destFile, true);
					} catch (Exception e) {
						UnityEngine.Debug.LogError($"[AddonInstaller] Failed to copy file '{file}' to '{destFile}': {e.Message}");
						throw;
					}
				}
			}
			
			string[] subdirs = null;
			try {
				subdirs = Directory.GetDirectories(sourceDir);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to get subdirectories from source: {e.Message}");
				throw;
			}
			
			if (subdirs != null) {
				foreach (string subdir in subdirs) {
					string dirName = null;
					try {
						dirName = Path.GetFileName(subdir);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to get subdir name: {e.Message}");
						continue;
					}
					
					if (string.IsNullOrEmpty(dirName)) {
						continue;
					}
					
					string destSubdir = null;
					try {
						destSubdir = Path.Combine(destDir, dirName);
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to combine dest subdir path: {e.Message}");
						continue;
					}
					
					CopyDirectoryRecursive(subdir, destSubdir);
				}
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
			if (!_removeInFlight.Add(addonId)) {
				onComplete?.Invoke(false, $"Removal already in progress for '{addonId}'");
				return;
			}
			StartCoroutine(RemoveAddonCrtn(addonId, onComplete));
		}

		IEnumerator RemoveAddonCrtn(string addonId, Action<bool, string> onComplete) {
			try {
				yield return RemoveAddonCrtnBody(addonId, onComplete);
			} finally {
				_removeInFlight.Remove(addonId);
			}
		}

		IEnumerator RemoveAddonCrtnBody(string addonId, Action<bool, string> onComplete) {
			if (string.IsNullOrEmpty(addonId)) {
				onComplete?.Invoke(false, "Invalid add-on ID");
				yield break;
			}
			
			// Check if streamingAssetsPath is valid first
			if (string.IsNullOrEmpty(Application.streamingAssetsPath)) {
				UnityEngine.Debug.LogError("[AddonInstaller] Application.streamingAssetsPath is null or empty");
				onComplete?.Invoke(false, "StreamingAssets path is not available");
				yield break;
			}
			
			string addonPath = null;
			try {
				addonPath = Path.Combine(Application.streamingAssetsPath, "Addons", addonId);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to combine addon path: {e.Message}");
				onComplete?.Invoke(false, $"Failed to construct addon path: {e.Message}");
				yield break;
			}
			
			if (string.IsNullOrEmpty(addonPath)) {
				onComplete?.Invoke(false, "Addon path is null or empty");
				yield break;
			}
			
			bool dirExists = false;
			try {
				dirExists = Directory.Exists(addonPath);
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Failed to check if addon directory exists: {e.Message}");
				onComplete?.Invoke(false, $"Failed to check addon directory: {e.Message}");
				yield break;
			}
			
			if (!dirExists) {
				onComplete?.Invoke(false, $"Add-on '{addonId}' not found");
				yield break;
			}
			
			// Must finish Python unregister + UI tear-down before deleting the folder (HTTP unload is async).
			// If unload times out (server down), still delete the folder — otherwise Uninstall appears dead forever.
			if (Addon_MGR.instance != null) {
				bool unloadDone = false;
				Addon_MGR.instance.UnloadAddon(addonId, () => unloadDone = true);
				float waitUnload = 0f;
				const float unloadTimeoutSec = 45f;
				while (!unloadDone && waitUnload < unloadTimeoutSec) {
					waitUnload += Time.unscaledDeltaTime;
					yield return null;
				}
				if (!unloadDone) {
					UnityEngine.Debug.LogWarning(
						$"[AddonInstaller] Unity unload for '{addonId}' timed out — proceeding with folder delete.");
				}
				float waitPending = 0f;
				const float pendingTimeoutSec = 45f;
				while (Addon_MGR.instance != null
				       && Addon_MGR.instance.IsPythonUnloadPending(addonId)
				       && waitPending < pendingTimeoutSec) {
					waitPending += Time.unscaledDeltaTime;
					yield return null;
				}
				if (Addon_MGR.instance != null && Addon_MGR.instance.IsPythonUnloadPending(addonId)) {
					UnityEngine.Debug.LogWarning(
						$"[AddonInstaller] Python unload for '{addonId}' still pending — proceeding with folder delete.");
				}
			}
			
			try {
				try {
					Directory.Delete(addonPath, true);
				} catch (Exception e) {
					UnityEngine.Debug.LogError($"[AddonInstaller] Failed to delete addon directory: {e.Message}");
					onComplete?.Invoke(false, $"Removal failed: {e.Message}");
					yield break;
				}
				
				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.DiscoverAddons();
				}
				
				onComplete?.Invoke(true, $"Add-on '{addonId}' removed successfully");
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[AddonInstaller] Error removing add-on: {e.Message}");
				onComplete?.Invoke(false, $"Removal failed: {e.Message}");
			}
		}

		/// <summary>Canonical path for zip install (editor / tools).</summary>
		public static string NormalizeZipPathForInstall(string zipPath) {
			if (string.IsNullOrEmpty(zipPath)) {
				return null;
			}
			try {
				return Path.GetFullPath(zipPath);
			} catch {
				return zipPath;
			}
		}

		/// <summary>Extracts a zip to an existing or new directory (overwrite files).</summary>
		public static void ExtractZipToDirectorySafe(string zipFilePath, string destinationDirectory) {
			if (string.IsNullOrEmpty(zipFilePath) || string.IsNullOrEmpty(destinationDirectory)) {
				throw new ArgumentException("Zip path and destination directory are required.");
			}
			Directory.CreateDirectory(destinationDirectory);
			ZipFile.ExtractToDirectory(zipFilePath, destinationDirectory, overwriteFiles: true);
		}

		/// <summary>
		/// Copies a resolved add-on folder into <paramref name="addonsBasePath"/>/&lt;id&gt; (backs up existing).
		/// Used by runtime installer logic and <c>AddonZipSceneViewInstallHook</c>.
		/// </summary>
		public static bool TryPublishAddonRootToStreamingAssets(string addonRoot, string addonsBasePath, out string addonId, out string err) {
			addonId = null;
			err = null;
			string targetPath = null;
			string backupPath = null;
			try {
				if (string.IsNullOrEmpty(addonRoot) || !Directory.Exists(addonRoot)) {
					err = "Add-on root is missing or invalid.";
					return false;
				}
				if (string.IsNullOrEmpty(addonsBasePath)) {
					err = "Addons destination path is invalid.";
					return false;
				}
				Directory.CreateDirectory(addonsBasePath);

				addonId = GetAddonIdFromRoot(addonRoot);
				if (string.IsNullOrEmpty(addonId)) {
					try {
						addonId = Path.GetFileName(addonRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Failed to get folder name for add-on id: {e.Message}");
					}
				}
				if (string.IsNullOrEmpty(addonId)) {
					addonId = $"Addon_{DateTime.Now:yyyyMMdd_HHmmss}";
				}

				try {
					targetPath = Path.Combine(addonsBasePath, addonId);
				} catch (Exception e) {
					err = $"Failed to build target path: {e.Message}";
					return false;
				}

				if (Directory.Exists(targetPath)) {
					try {
						backupPath = $"{targetPath}_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
						Directory.Move(targetPath, backupPath);
						UnityEngine.Debug.Log($"[AddonInstaller] Backed up existing add-on to {backupPath}");
					} catch (Exception e) {
						err = $"Failed to backup existing add-on: {e.Message}";
						return false;
					}
				}

				try {
					CopyDirectoryRecursive(addonRoot, targetPath);
				} catch (Exception e) {
					err = $"Failed to copy add-on files: {e.Message}";
					TryRestoreAddonBackup(targetPath, backupPath);
					return false;
				}

				string initFile = Path.Combine(targetPath, "__init__.py");
				if (!File.Exists(initFile)) {
					err = "Installation failed: __init__.py not found after copy.";
					try {
						if (Directory.Exists(targetPath)) {
							Directory.Delete(targetPath, true);
						}
					} catch (Exception e) {
						UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not remove partial install: {e.Message}");
					}
					TryRestoreAddonBackup(targetPath, backupPath);
					return false;
				}

				if (Addon_MGR.instance != null) {
					Addon_MGR.instance.DiscoverAddons();
				}
				return true;
			} catch (Exception e) {
				err = e.Message;
				TryRestoreAddonBackup(targetPath, backupPath);
				return false;
			}
		}

		static void TryRestoreAddonBackup(string targetPath, string backupPath) {
			if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath)) {
				return;
			}
			if (string.IsNullOrEmpty(targetPath)) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Backup remains at {backupPath} (target path unknown).");
				return;
			}
			try {
				if (Directory.Exists(targetPath)) {
					Directory.Delete(targetPath, true);
				}
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not remove failed install before restore: {e.Message}");
			}
			if (Directory.Exists(targetPath)) {
				return;
			}
			try {
				Directory.Move(backupPath, targetPath);
				UnityEngine.Debug.Log($"[AddonInstaller] Restored original add-on from backup to {targetPath}");
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning($"[AddonInstaller] Could not restore from backup: {e.Message}. Original remains at {backupPath}");
			}
		}
	}
}
