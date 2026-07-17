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

		static bool s_restartInProgress;

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
				// attachToConsole:false — FreeConsole/AttachConsole on the Unity process during restart can stall or crash.
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
					path, isJustFile: true, workDir, keepWindow: false, hidden: false, attachToConsole: false);
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

		/// <summary>
		/// Launch Run_with_Addons.bat then quit so the bat starts the game with addons.
		/// Does not hard-kill immediately (that raced the exit-confirm popup and caused stalls/crashes).
		/// Uses <see cref="ExitTheProgram_MGR.AllowQuitWithoutConfirmAndArmWatchdog"/> so quit is not blocked,
		/// then the existing force-exit watchdog covers hung shutdown.
		/// </summary>
		public static void RestartWithAddons() {
			if (s_restartInProgress) {
				Debug.LogWarning("[Launch_Addons] Restart with addons already in progress — ignoring duplicate click.");
				return;
			}

#if UNITY_EDITOR
			// Never Application.Quit / Environment.Exit the Editor — that stalls Play Mode and can corrupt the session.
			s_restartInProgress = true;
			bool launched = LaunchAddonsBat(showStatusIfNotFound: true);
			if (AddonManager_UI.instance != null) {
				if (launched)
					AddonManager_UI.instance.ShowRestartStatus(
						$"Launched {DefaultBatName}. Stop Play Mode and run the player via that bat (Editor was not quit).",
						true);
				else
					AddonManager_UI.instance.ShowRestartStatus($"{DefaultBatName} not found — cannot restart with addons.", false);
			} else if (Viewport_StatusText.instance != null && launched) {
				Viewport_StatusText.instance.ShowStatusText(
					$"Launched {DefaultBatName}. Stop Play Mode; use the bat for the player build.", false, 6f, false);
			}
			s_restartInProgress = false;
			return;
#else
			s_restartInProgress = true;

			// Teardown sockets/Python before spawning the bat so ports/files are free for the next instance.
			Addon_MGR.ShutdownAddonApiBeforeQuit();

			if (!LaunchAddonsBat(showStatusIfNotFound: true)) {
				s_restartInProgress = false;
				if (AddonManager_UI.instance != null)
					AddonManager_UI.instance.ShowRestartStatus(
						$"{DefaultBatName} not found — cannot restart with addons.", false);
				return;
			}

			if (AddonManager_UI.instance != null) {
				AddonManager_UI.instance.ShowRestartStatus("Restarting with addons…", true);
				AddonManager_UI.instance.ClosePanel();
			}

			// Bypass "Close the program?" — previous code called Quit (blocked by popup) then Environment.Exit (hard crash).
			ExitTheProgram_MGR.AllowQuitWithoutConfirmAndArmWatchdog();
			Application.Quit();
			// Do not Environment.Exit here. Watchdog force-exits only if Unity shutdown stalls (~4s).
#endif
		}

		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
		}
	}
}
