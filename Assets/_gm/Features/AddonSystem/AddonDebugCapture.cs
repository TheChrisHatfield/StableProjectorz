using System;
using System.IO;
using UnityEngine;

namespace spz {

	/// <summary>
	/// Captures addon-related Unity logs to a file so you can share AddonDebug.log after a run for debugging.
	/// Log is saved next to the project (Editor) or next to the .exe (build), not on C: AppData.
	/// File is created as soon as the game starts (before any scene loads) so it always exists when the exe runs.
	/// </summary>
	public class AddonDebugCapture : MonoBehaviour {
		public static AddonDebugCapture instance { get; private set; }

		static readonly string[] CaptureTags = { "[Addon_MGR]", "[Addon_SocketServer]", "[AddonUI_MGR]", "[CommandRibbon_UI]", "[AddonDebugCapture]" };
		static string _logPath;
		static StreamWriter _writer;
		static readonly object _lock = new object();
		static bool _staticInitialized;

		public static string GetLogFilePath() {
			if (!string.IsNullOrEmpty(_logPath)) return _logPath;
			// Same drive as project/build: Editor = project root, Build = folder containing the exe
			string baseDir = Path.GetDirectoryName(Application.dataPath);
			_logPath = Path.Combine(baseDir ?? Application.dataPath, "AddonDebug.log");
			return _logPath;
		}

		/// <summary>Runs as soon as the game (or Editor play) starts, before any scene loads. Creates AddonDebug.log immediately.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void CreateLogFileAtLaunch() {
			if (_staticInitialized) return;
			_staticInitialized = true;
			GetLogFilePath();
			lock (_lock) {
				try {
					_writer = new StreamWriter(_logPath, append: true);
					_writer.WriteLine($"=== Game launched {DateTime.Now:yyyy-MM-dd HH:mm:ss} (log created at startup) ===");
					_writer.WriteLine($"Log file: {_logPath}");
					_writer.Flush();
					Application.logMessageReceived += OnLogStatic;
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[AddonDebugCapture] Could not create log file at launch: {e.Message}");
				}
			}
		}

		static void OnLogStatic(string condition, string stackTrace, LogType type) {
			for (int i = 0; i < CaptureTags.Length; i++) {
				if (condition.IndexOf(CaptureTags[i], StringComparison.Ordinal) >= 0) {
					WriteLine($"[{type}] {condition}");
					break;
				}
			}
		}

		void Awake() {
			if (instance != null) { Destroy(gameObject); return; }
			instance = this;
			DontDestroyOnLoad(gameObject);
			// Ensure log exists and is capturing (may already be done by CreateLogFileAtLaunch)
			if (!_staticInitialized) CreateLogFileAtLaunch();
			else if (_writer != null) {
				lock (_lock) {
					try {
						_writer.WriteLine($"=== Addon system loaded {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
						_writer.Flush();
					} catch { }
				}
			}
			UnityEngine.Debug.Log($"[AddonDebugCapture] Addon debug log file: {GetLogFilePath()}");
		}

		void OnDestroy() {
			Application.logMessageReceived -= OnLogStatic;
			lock (_lock) {
				try {
					_writer?.Dispose();
				} catch { }
				_writer = null;
			}
		}

		static void WriteLine(string line) {
			lock (_lock) {
				if (_writer == null) return;
				try {
					_writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
					_writer.Flush();
				} catch { }
			}
		}

		/// <summary>Call after "Load addons now" to write a marker and flush.</summary>
		public static void MarkLoadAddonsFinished() {
			WriteLine("--- Load addons now finished (check above for create_panel / tab creation) ---");
		}
	}
}
