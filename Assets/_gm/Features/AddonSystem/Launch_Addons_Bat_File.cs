using System;
using System.IO;
using UnityEngine;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Lavender.Systems;
#endif

namespace spz {

	/// <summary>
	/// Finds and launches Run_with_Addons.bat the same way Run_noQuickEdit is used for WebUI:
	/// discover next to exe / parent dirs / env SPZ_ADDONS_RUN_PATH, then Run_Bat_or_Shortcut_or_Command.
	/// Use for "Restart with addons" (launch bat then quit so the bat starts the game with Python on PATH).
	/// </summary>
	public class Launch_Addons_Bat_File : MonoBehaviour {
		public static Launch_Addons_Bat_File instance { get; private set; }

		const string DefaultBatName = "Run_with_Addons.bat";
		const string EnvVarName = "SPZ_ADDONS_RUN_PATH";

		/// <summary>Finds Run_with_Addons.bat (exe dir, parent dirs, or env). Returns "" if not found.</summary>
		public static string GetAddonsBatFilePath(bool logIfNotFound = false) {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
			return "";
#else
			try {
				string envPath = Environment.GetEnvironmentVariable(EnvVarName);
				if (!string.IsNullOrWhiteSpace(envPath)) {
					string trimmed = envPath.Trim();
					if (File.Exists(trimmed)) {
						Debug.Log($"[Launch_Addons] Bat found via {EnvVarName}: {trimmed}");
						return trimmed;
					}
				}
			} catch (Exception e) {
				Debug.LogWarning($"[Launch_Addons] Could not check {EnvVarName}: {e.Message}");
			}

			string exeDir = Path.GetDirectoryName(Application.dataPath);
			if (string.IsNullOrEmpty(exeDir)) return "";

			// Bat lives in project root; exe often in Build_IL2CPP, so check exe dir and parent
			string[] candidates = new string[] {
				Path.Combine(exeDir, DefaultBatName),
				Path.Combine(exeDir, "..", DefaultBatName),
				Path.Combine(exeDir, "..", "..", DefaultBatName),
			};

			foreach (string p in candidates) {
				try {
					string full = Path.GetFullPath(p);
					if (File.Exists(full)) {
						Debug.Log($"[Launch_Addons] Bat found: {full}");
						return full;
					}
				} catch { /* skip */ }
			}

			if (logIfNotFound)
				Debug.Log($"[Launch_Addons] {DefaultBatName} not found. Searched: {exeDir}. Optional: set {EnvVarName} to full path.");
			return "";
#endif
		}

		/// <summary>Launch Run_with_Addons.bat (same pattern as LaunchWebUI). Returns true if launched.</summary>
		public static bool LaunchAddonsBat(bool showStatusIfNotFound = false) {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
			if (showStatusIfNotFound && Viewport_StatusText.instance != null)
				Viewport_StatusText.instance.ShowStatusText("Run with addons is only supported on Windows.", false, 3f, false);
			return false;
#else
			string path = GetAddonsBatFilePath(logIfNotFound: true);
			if (string.IsNullOrEmpty(path)) {
				if (showStatusIfNotFound && Viewport_StatusText.instance != null)
					Viewport_StatusText.instance.ShowStatusText($"{DefaultBatName} not found. Use it to start the game for addon support.", false, 5f, false);
				return false;
			}
			try {
				string workDir = Path.GetDirectoryName(path);
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(path, isJustFile: true, workDir);
				if (pid != 0) {
					Debug.Log($"[Launch_Addons] Launched {DefaultBatName} PID {pid}");
					return true;
				}
				Debug.LogError("[Launch_Addons] Failed to launch process.");
				return false;
			} catch (Exception e) {
				Debug.LogError($"[Launch_Addons] Error launching: {e.Message}");
				return false;
			}
#endif
		}

		/// <summary>Launch Run_with_Addons.bat then exit so the bat starts the game with addons. Force-exit so the current instance actually closes (Application.Quit alone can leave it running).</summary>
		public static void RestartWithAddons() {
			if (!LaunchAddonsBat(showStatusIfNotFound: true)) return;
			Application.Quit();
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			// Force process exit; Application.Quit() may not actually close the app
			System.Environment.Exit(0);
#endif
		}

		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
	}
}
