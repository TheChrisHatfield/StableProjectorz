using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using System.Diagnostics;
#endif
using Newtonsoft.Json.Linq;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Lavender.Systems;
#endif

namespace spz {

	/// <summary>Optional manifest beside <c>__init__.py</c> for Add-on Manager list (reference-style “v1.0 • description”).</summary>
	[Serializable]
	public class AddonJsonManifest {
		public string version;
		public string description;
		/// <summary>Short label in Add-on Manager (optional; else folder <c>addonId</c>).</summary>
		public string displayName;
	}

	/// <summary>Kill any process listening on the given port (Windows). Avoids [Errno 10048] when starting FastAPI on 5557. Never kills Unity.exe or Unity Hub.</summary>
	internal static class AddonPortHelper
	{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		/// <summary>Returns true if the process for this PID is one we must never kill: Unity.exe, Unity Hub.exe, or StableProjectorz.exe (our own game).
		/// Returns true on ANY failure (fail-safe: when in doubt, don't kill).</summary>
		static bool IsProcessUnityOrHub(uint processId)
		{
			string tempFile = Path.Combine(Path.GetTempPath(), "spz_tasklist_" + processId + "_" + Guid.NewGuid().ToString("N") + ".txt");
			string workDir = Path.GetTempPath();
			try
			{
				string cmd = "tasklist /FI \"PID eq " + processId + "\" > \"" + tempFile + "\"";
				uint cmdPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
				if (cmdPid == 0) return true;
				StartExternalProcess.WaitForProcessExit(cmdPid, 1500);
				if (!File.Exists(tempFile)) return true;
				string output = File.ReadAllText(tempFile);
				if (output.IndexOf("Unity.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				if (output.IndexOf("Unity Hub.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				if (output.IndexOf("StableProjectorz.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
				return false;
			}
			catch { return true; }
			finally { try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { } }
		}

		/// <summary>Finds PIDs listening on the port via netstat -ano, then taskkill /PID x /F. Never kills Unity.exe or Unity Hub. Uses StartExternalProcess so it works in IL2CPP build.</summary>
		public static void TryKillProcessesOnPort(int port)
		{
			// Port 5555 is the Unity/addon socket; never free it from here (Editor may be using it; we never kill Unity).
			if (port == 5555) return;
			string tempFile = Path.Combine(Path.GetTempPath(), "spz_netstat_" + port + "_" + Guid.NewGuid().ToString("N") + ".txt");
			string workDir = Path.GetTempPath();
			try
			{
				// Use colon prefix ":{port}" so we match the port in the address column (e.g. "127.0.0.1:5557")
				// and never false-positive on PIDs that happen to contain the port number as a substring.
				string cmd = "netstat -ano | find \":" + port + "\" > \"" + tempFile + "\"";
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
				if (pid == 0) return;
				StartExternalProcess.WaitForProcessExit(pid, 2000);
				if (!File.Exists(tempFile)) return;
				string output = File.ReadAllText(tempFile);
				// netstat -ano line e.g. "  TCP    127.0.0.1:5557    0.0.0.0:0    LISTENING    12345"
				// parts[1] = local address (e.g. "127.0.0.1:5557"), parts[last] = PID
				string portSuffix = ":" + port;
				var pids = new HashSet<uint>();
				foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string trimmed = line.Trim();
					if (string.IsNullOrEmpty(trimmed) || !trimmed.Contains("LISTENING")) continue;
					string[] parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length < 4) continue;
					// Verify the local address column actually ends with the exact port
					// (prevents matching port 55570 when looking for 5557)
					if (!parts[1].EndsWith(portSuffix)) continue;
					if (uint.TryParse(parts[parts.Length - 1], out uint p))
						pids.Add(p);
				}
				uint currentPid = StartExternalProcess.GetCurrentPid();
				foreach (uint p in pids)
				{
					if (p == currentPid)
					{
						UnityEngine.Debug.Log($"[Addon_MGR] Skipping current process PID {p} (Unity/game window); not killing self.");
						continue;
					}
					// Never kill Unity Editor or Unity Hub, regardless of port (safeguard against any matching bug)
					if (IsProcessUnityOrHub(p))
					{
						UnityEngine.Debug.Log($"[Addon_MGR] Skipping PID {p} (Unity/Unity Hub); will not kill Editor.");
						continue;
					}
					string killCmd = "taskkill /PID " + p + " /F";
					uint killPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(killCmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
					if (killPid != 0)
					{
						StartExternalProcess.WaitForProcessExit(killPid, 1500);
						UnityEngine.Debug.Log($"[Addon_MGR] Freed port {port}: killed process PID {p} (was holding 127.0.0.1:{port}).");
					}
				}
			}
			catch (Exception e)
			{
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Could not free port {port}: {e.Message}");
			}
			finally
			{
				try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
			}
		}
#endif
	}

	/// <summary>
	/// Manages add-on discovery, lifecycle, and Python server process.
	/// </summary>
	[DefaultExecutionOrder(-100)]  // Run before Addon_SocketServer (0) so instance exists when socket binds
	public class Addon_MGR : MonoBehaviour {
		public static Addon_MGR instance { get; private set; }

		/// <summary>Fired when an addon's enabled state was changed by the system (e.g. load failure). UI should refresh the list.</summary>
		public static event Action<string> OnAddonEnabledStateChanged;

		[SerializeField] string _pythonServerScript = "addon_server.py";
		[SerializeField] int _serverPort = 5555;
		[SerializeField] int _httpServerPort = 5557;
		[SerializeField] int _webSocketPort = 5558;
		[SerializeField] bool _enableHttpServer = true; // FastAPI in Python (recommended)
		[SerializeField] bool _enableCSharpHttpServer = false; // Legacy C# HttpListener (deprecated)
		[SerializeField] bool _enableWebSocketServer = false;
		[SerializeField] bool _autoRestartWithAddonsOnServerFail = false; // Avoid surprise relaunch/flicker in player unless explicitly enabled.
		
#if UNITY_EDITOR
		private Process _pythonProcess;
#endif
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		private uint _pythonServerPid; // When started via StartExternalProcess (IL2CPP-safe path)
#endif
		private Dictionary<string, AddonInfo> _registeredAddons = new Dictionary<string, AddonInfo>();
		private bool _isServerRunning = false;

		static bool s_addonApiQuitShutdownDone;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		static void ResetAddonQuitStatics() {
			s_addonApiQuitShutdownDone = false;
		}

		static void HandleApplicationQuitting() {
			ShutdownAddonApiBeforeQuit();
		}

		/// <summary>
		/// Stops add-on API in order: legacy C# HTTP (if any), Python FastAPI/socket client process, then Unity TCP listener.
		/// Idempotent. Call from <see cref="Application.wantsToQuit"/> (after user confirms exit) and/or rely on <see cref="Application.quitting"/>.
		/// </summary>
		public static void ShutdownAddonApiBeforeQuit() {
			if (s_addonApiQuitShutdownDone)
				return;
			s_addonApiQuitShutdownDone = true;
			try {
				if (Addon_HttpServer.instance != null)
					Addon_HttpServer.instance.ShutdownForQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (HTTP): " + e.Message);
			}
			try {
				if (instance != null)
					instance.TerminatePythonAddonServerProcess();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (Python): " + e.Message);
			}
			try {
				if (Addon_SocketServer.instance != null)
					Addon_SocketServer.instance.ShutdownNetworkingForQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (socket): " + e.Message);
			}
			try {
				FastPath_API.TryDeleteSpzGoSessionFolderOnQuit();
			} catch (Exception e) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] ShutdownAddonApiBeforeQuit (SPZ GO session dir): " + e.Message);
			}
		}

		void TerminatePythonAddonServerProcess() {
#if UNITY_EDITOR
			if (_pythonProcess != null) {
				try {
					_pythonProcess.CancelOutputRead();
					_pythonProcess.CancelErrorRead();
				} catch { }
				if (!_pythonProcess.HasExited) {
					try { _pythonProcess.Kill(); } catch { }
				}
				try { _pythonProcess.Dispose(); } catch { }
				_pythonProcess = null;
			}
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
			if (_pythonServerPid != 0) {
				try {
					string workDir = Path.GetTempPath();
					string cmd = "taskkill /PID " + _pythonServerPid + " /T /F";
					uint killPid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(cmd, isJustFile: false, workDir, keepWindow: false, hidden: true, attachToConsole: false);
					if (killPid != 0)
						StartExternalProcess.WaitForProcessExit(killPid, 3000);
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning("[Addon_MGR] Could not terminate Python addon server: " + e.Message);
				}
				_pythonServerPid = 0;
			}
#endif
			_isServerRunning = false;
		}
		
		public class AddonInfo {
			public string id;
			public string path;
			public bool isEnabled;
			public List<GameObject> uiElements = new List<GameObject>();
			/// <summary>Optional; from <c>addon.json</c> <c>displayName</c> (Add-on Manager row title).</summary>
			public string displayName;
			/// <summary>List row subtitle: e.g. <c>v1.2.0 • Advanced camera controls…</c> from <c>addon.json</c> or <c>__init__.py</c>.</summary>
			public string listSubtitle;
		}
		
		void Awake() {
			UnityEngine.Debug.Log("[Addon_MGR] Awake (Tool_AddonSystem scene loaded).");
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			// If the scene's Addon_SocketServer is missing (e.g. broken script ref in build), create it at runtime so Python can always connect.
			if (Addon_SocketServer.instance == null) {
				var go = new GameObject("Addon_SocketServer_Runtime");
				go.AddComponent<Addon_SocketServer>();
				UnityEngine.Debug.Log("[Addon_MGR] Addon_SocketServer was missing in scene; created at runtime so listener can bind.");
			}
			if (AddonUI_MGR.instance == null) {
				var uiMgr = FindObjectOfType<AddonUI_MGR>(true);
				if (uiMgr == null) {
					var go = new GameObject("AddonUI_MGR_Runtime");
					go.AddComponent<AddonUI_MGR>();
					UnityEngine.Debug.Log("[Addon_MGR] AddonUI_MGR was missing in scene; created at runtime so addon panels can be built.");
				}
			}
			// Zip install / remove (drag-drop, Add-on Manager) — same connectivity as socket + UI.
			if (AddonInstaller_MGR.instance == null) {
				var installer = FindObjectOfType<AddonInstaller_MGR>(true);
				if (installer == null) {
					var go = new GameObject("AddonInstaller_MGR_Runtime");
					go.transform.SetParent(transform, false);
					go.AddComponent<AddonInstaller_MGR>();
					UnityEngine.Debug.Log("[Addon_MGR] AddonInstaller_MGR was missing in scene; created at runtime for zip install / remove.");
				}
			}
			if (GetComponent<AddonDebugCapture>() == null)
				gameObject.AddComponent<AddonDebugCapture>();
			if (GetComponent<Launch_Addons_Bat_File>() == null)
				gameObject.AddComponent<Launch_Addons_Bat_File>();
			Application.quitting += HandleApplicationQuitting;
		}
		
		void Start() {
			StartCoroutine(InitializeAddonSystem());
		}
		
		IEnumerator InitializeAddonSystem() {
			// Start addon system immediately on exe start (do not block on FastPath_API).
			// FastPath_API is only needed for addon commands (camera/mesh); Addon_SocketServer returns "FastPath_API not ready" until then.
			UnityEngine.Debug.Log("[Addon_MGR] Addon system initializing: discovering addons and starting Python server.");
			
			// Discover add-ons
			DiscoverAddons();
			
			// Wait until Addon_SocketServer is listening on 5555 so Python can connect (avoids WinError 10061 "connection refused").
			// Execution order: Addon_MGR.Start runs first and yields; Addon_SocketServer.Start then binds; next frame we check and start Python.
			yield return null;
			const int maxWaitFrames = 120;  // ~2 s at 60 fps
			for (int i = 0; i < maxWaitFrames; i++) {
				if (Addon_SocketServer.instance != null && Addon_SocketServer.instance.IsListening) {
					if (i > 2) UnityEngine.Debug.Log("[Addon_MGR] Socket server is listening; starting Python.");
					break;
				}
				if (i == 0) UnityEngine.Debug.Log("[Addon_MGR] Waiting for addon socket server to bind to port...");
				if (i == maxWaitFrames - 1) {
					UnityEngine.Debug.LogError("[Addon_MGR] Addon_SocketServer not listening after " + maxWaitFrames + " frames. Is the addon socket server GameObject active in the scene? Python will likely fail to connect (WinError 10061).");
				}
				yield return null;
			}
			
			// Start Python server (includes FastAPI HTTP server if enabled)
			StartPythonServer();
			
			// If server failed and we have addons, auto-restart via Run_with_Addons.bat (built player only; never in Editor to avoid quitting Unity)
			if (_autoRestartWithAddonsOnServerFail && !_isServerRunning && _enableHttpServer && HasAnyEnabledAddon() && !WasLaunchedByAddonsBat() && !Application.isEditor) {
				StartCoroutine(AutoRestartWithAddonsAfterDelay());
				yield break;
			}
			
			// Request Python to load each enabled addon (server exposes POST /load_addon)
			if (_enableHttpServer) {
				StartCoroutine(RequestLoadEnabledAddonsAfterDelay());
			}
			
			// Start legacy C# HTTP server if enabled (deprecated - use FastAPI instead)
			if (_enableCSharpHttpServer && Addon_HttpServer.instance == null) {
				GameObject httpServerObj = new GameObject("Addon_HttpServer");
				httpServerObj.AddComponent<Addon_HttpServer>();
				UnityEngine.Debug.LogWarning("[Addon_MGR] Using legacy C# HTTP server. Consider using FastAPI instead (enabled by default in Python server).");
			}
		}
		
		/// <summary>True if we were started by Run_with_Addons.bat (bat sets SPZ_ADDONS_LAUNCHED=1). Used to avoid auto-restart loop.</summary>
		static bool WasLaunchedByAddonsBat() {
			try {
				string v = Environment.GetEnvironmentVariable("SPZ_ADDONS_LAUNCHED");
				return !string.IsNullOrWhiteSpace(v);
			} catch { return false; }
		}
		
		bool HasAnyEnabledAddon() {
			foreach (var kvp in _registeredAddons) { if (kvp.Value.isEnabled) return true; }
			return false;
		}
		
		IEnumerator AutoRestartWithAddonsAfterDelay() {
			UnityEngine.Debug.Log("[Addon_MGR] Python server did not start; auto-launching Run_with_Addons.bat so the game runs with addons (one-time restart).");
			yield return new WaitForSeconds(1.5f);
			if (Launch_Addons_Bat_File.GetAddonsBatFilePath(logIfNotFound: false).Length > 0) {
				Launch_Addons_Bat_File.RestartWithAddons();
			} else {
				UnityEngine.Debug.LogWarning("[Addon_MGR] Run_with_Addons.bat not found. Start the game with that bat for addon support.");
			}
		}
		
		static void EnrichAddonListSubtitle(AddonInfo info) {
			if (info == null) return;
			if (string.IsNullOrEmpty(info.path) || !Directory.Exists(info.path)) {
				info.listSubtitle = "Installed add-on";
				return;
			}
			string ver = null;
			string desc = null;
			string jsonPath = Path.Combine(info.path, "addon.json");
			if (File.Exists(jsonPath)) {
				try {
					string json = File.ReadAllText(jsonPath);
					var m = JsonUtility.FromJson<AddonJsonManifest>(json);
					if (m != null) {
						if (!string.IsNullOrWhiteSpace(m.version)) ver = m.version.Trim();
						if (!string.IsNullOrWhiteSpace(m.description)) desc = m.description.Trim();
						if (!string.IsNullOrWhiteSpace(m.displayName)) info.displayName = m.displayName.Trim();
					}
				} catch (Exception e) {
					UnityEngine.Debug.LogWarning($"[Addon_MGR] addon.json read failed for {info.id}: {e.Message}");
				}
			}
			TryParseInitPyMetadata(Path.Combine(info.path, "__init__.py"), ref ver, ref desc);
			info.listSubtitle = BuildAddonListSubtitle(ver, desc, info.path);
		}
		
		static void TryParseInitPyMetadata(string initPath, ref string ver, ref string desc) {
			if (!File.Exists(initPath)) return;
			string text;
			try {
				text = File.ReadAllText(initPath);
			} catch {
				return;
			}
			if (text.Length > 24576)
				text = text.Substring(0, 24576);
			if (string.IsNullOrEmpty(ver)) {
				var vm = Regex.Match(text, @"__version__\s*=\s*[""']([^""']+)[""']");
				if (vm.Success)
					ver = vm.Groups[1].Value.Trim();
			}
			if (!string.IsNullOrEmpty(desc)) return;
			int a = text.IndexOf("\"\"\"", StringComparison.Ordinal);
			if (a < 0) return;
			int b = text.IndexOf("\"\"\"", a + 3, StringComparison.Ordinal);
			if (b <= a) return;
			string body = text.Substring(a + 3, b - a - 3).Trim();
			var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			if (lines.Length == 0) return;
			string l0 = lines[0].Trim();
			if (lines.Length >= 2) {
				string l1 = lines[1].Trim();
				if (l1.Length > 0 && (l0.EndsWith("Add-on", StringComparison.OrdinalIgnoreCase) || l0.Length < 28))
					desc = l1;
				else
					desc = l0;
			} else {
				desc = l0;
			}
		}
		
		static string BuildAddonListSubtitle(string ver, string desc, string dirPath) {
			desc = NormalizeSubtitleLine(desc, 160);
			string vPart = null;
			if (!string.IsNullOrEmpty(ver)) {
				ver = ver.Trim();
				vPart = ver.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? ver : "v" + ver;
			}
			if (vPart != null && !string.IsNullOrEmpty(desc))
				return $"{vPart} • {desc}";
			if (vPart != null)
				return $"{vPart} • Installed add-on";
			if (!string.IsNullOrEmpty(desc))
				return desc;
			return $"Installed add-on · {Path.GetFileName(dirPath)}";
		}
		
		static string NormalizeSubtitleLine(string s, int maxLen) {
			if (string.IsNullOrEmpty(s)) return null;
			s = s.Replace('\r', ' ').Replace('\n', ' ').Trim();
			while (s.Contains("  "))
				s = s.Replace("  ", " ");
			if (s.Length > maxLen)
				s = s.Substring(0, maxLen - 1) + "…";
			return s;
		}
		
		/// <summary>
		/// Scans StreamingAssets/Addons/ for add-on directories
		/// </summary>
		public void DiscoverAddons() {
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				
				if (string.IsNullOrEmpty(addonsPath)) {
					UnityEngine.Debug.LogError("[Addon_MGR] StreamingAssets path is null or empty");
					return;
				}
				
				if (!Directory.Exists(addonsPath)) {
					try {
						Directory.CreateDirectory(addonsPath);
						UnityEngine.Debug.Log($"[Addon_MGR] Created Addons directory at {addonsPath}");
					} catch (System.Exception e) {
						UnityEngine.Debug.LogError($"[Addon_MGR] Failed to create Addons directory: {e.Message}");
						return;
					}
					return;
				}
				
				string[] addonDirs = null;
				try {
					addonDirs = Directory.GetDirectories(addonsPath);
				} catch (System.Exception e) {
					UnityEngine.Debug.LogError($"[Addon_MGR] Failed to get directories from {addonsPath}: {e.Message}");
					return;
				}
				
				UnityEngine.Debug.Log($"[Addon_MGR] Scanning for addons in: {addonsPath}");
				UnityEngine.Debug.Log($"[Addon_MGR] Found {addonDirs.Length} directories");
				
				var foundIds = new HashSet<string>();
				foreach (var dir in addonDirs) {
					try {
						string initFile = Path.Combine(dir, "__init__.py");
						string addonId = Path.GetFileName(dir);
						
						if (string.IsNullOrEmpty(addonId)) {
							continue;
						}
						
						if (File.Exists(initFile)) {
							foundIds.Add(addonId);
							// Update existing or add new; preserve enabled state when re-discovering. New addons default disabled (no persistence = all "new" each run; avoids auto-load storm and auto-restart loops when Python fails).
							bool wasEnabled = _registeredAddons.ContainsKey(addonId) ? _registeredAddons[addonId].isEnabled : false;
							_registeredAddons[addonId] = new AddonInfo {
								id = addonId,
								path = dir,
								isEnabled = wasEnabled
							};
							EnrichAddonListSubtitle(_registeredAddons[addonId]);
							UnityEngine.Debug.Log($"[Addon_MGR] Discovered add-on: {addonId} (enabled: {wasEnabled})");
						} else {
							UnityEngine.Debug.LogWarning($"[Addon_MGR] Directory '{addonId}' found but missing __init__.py, skipping");
						}
					} catch (System.Exception e) {
						UnityEngine.Debug.LogWarning($"[Addon_MGR] Error processing directory '{dir}': {e.Message}");
						continue;
					}
				}
				
				// Remove addons that are no longer on disk (uninstalled)
				var toRemove = new List<string>();
				foreach (var key in _registeredAddons.Keys) {
					if (!foundIds.Contains(key)) toRemove.Add(key);
				}
				foreach (var key in toRemove) {
					_registeredAddons.Remove(key);
				}
				
				UnityEngine.Debug.Log($"[Addon_MGR] Total addons discovered: {_registeredAddons.Count}");
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Fatal error in DiscoverAddons(): {e.Message}\n{e.StackTrace}");
			}
		}
		
		/// <summary>
		/// Resolves python.exe path so the addon server can start when the exe launches (dual-trigger),
		/// even if Python is not on PATH. Mirrors Run_with_Addons.bat search order.
		/// </summary>
		static string TryResolvePythonExe() {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
			string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
			string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
			string[] candidates = new string[] {
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe") : null,
				localAppData != null ? Path.Combine(localAppData, "Programs", "Python", "Python313", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python312", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python311", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python310", "python.exe") : null,
				programFiles != null ? Path.Combine(programFiles, "Python313", "python.exe") : null,
			};
			foreach (string p in candidates) {
				if (!string.IsNullOrEmpty(p) && File.Exists(p)) {
					UnityEngine.Debug.Log($"[Addon_MGR] Resolved Python: {p}");
					return p;
				}
			}
			UnityEngine.Debug.Log("[Addon_MGR] Python not found in standard locations; will try 'python' from PATH.");
#endif
			return null; // fallback: use "python" so PATH is used when available
		}

		/// <summary>
		/// Starts the Python server process (dual-trigger: runs automatically when exe loads, like quick-start flow).
		/// </summary>
		void StartPythonServer() {
			if (_isServerRunning) return;

			string serverScriptPath = null;
			try {
				serverScriptPath = Path.Combine(Application.streamingAssetsPath, "AddonSystem", _pythonServerScript);
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to construct server script path: {e.Message}");
				return;
			}
			
			bool fileExists = false;
			try {
				fileExists = File.Exists(serverScriptPath);
			} catch (System.Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to check if server script exists: {e.Message}");
				return;
			}
			
			if (!fileExists) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Python server script not found at {serverScriptPath}");
				return;
			}
			
			string pythonExe = TryResolvePythonExe();
			if (string.IsNullOrEmpty(pythonExe)) pythonExe = "python";
			UnityEngine.Debug.Log($"[Addon_MGR] Starting Python server: socket port {_serverPort}, HTTP port {_httpServerPort}, exe: {pythonExe}");
			
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			// IL2CPP: System.Diagnostics.Process.Start() triggers "Process::CreateProcess_internal" assertion.
			// Use Win32 CreateProcessW via StartExternalProcess (same as WebUI/addon bat launcher).
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				string workDir = Path.GetDirectoryName(serverScriptPath);
				string batPath = Path.Combine(workDir, "StartAddonServer.bat");
				string httpArg = _enableHttpServer ? $"--http-port {_httpServerPort}" : "--no-http";
				// Tell Python whether we bound 5555: if not (Editor has it), Python must NOT kill anything on 5557 or it may kill the Editor.
				string socketBound = (Addon_SocketServer.instance != null && Addon_SocketServer.instance.IsListening) ? "1" : "0";
				string batContent = "@echo off\r\ncd /d \"" + workDir + "\"\r\nset SPZ_SOCKET_BOUND=" + socketBound + "\r\n\"" + pythonExe.Replace("\"", "\"\"") + "\" \"" + serverScriptPath.Replace("\"", "\"\"") + "\" --port " + _serverPort + " --addons-dir \"" + addonsPath.Replace("\"", "\"\"") + "\" " + httpArg + "\r\n";
				File.WriteAllText(batPath, batContent);
				UnityEngine.Debug.Log("[Addon_MGR] Starting addon server in background (hidden console).");
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(
					batPath,
					isJustFile: true,
					workDir,
					keepWindow: false,
					hidden: true,
					attachToConsole: false
				);
				if (pid != 0) {
					_pythonServerPid = pid;
					_isServerRunning = true;
					UnityEngine.Debug.Log($"[Addon_MGR] Python server started on port {_serverPort} (PID {pid})");
				} else {
					UnityEngine.Debug.LogError("[Addon_MGR] Failed to start Python server (CreateProcess returned 0).");
				}
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to start Python server: {e.Message}");
			}
#elif UNITY_EDITOR
			try {
				string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
				string arguments = $"\"{serverScriptPath}\" --port {_serverPort} --addons-dir \"{addonsPath}\"";
				if (_enableHttpServer) {
					arguments += $" --http-port {_httpServerPort}";
				} else {
					arguments += " --no-http";
				}
				_pythonProcess = new Process {
					StartInfo = new ProcessStartInfo {
						FileName = pythonExe,
						Arguments = arguments,
						UseShellExecute = false,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						CreateNoWindow = false,
						WorkingDirectory = Path.GetDirectoryName(serverScriptPath)
					}
				};
				_pythonProcess.OutputDataReceived += (sender, e) => {
					if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"[Python Server] {e.Data}");
				};
				_pythonProcess.ErrorDataReceived += (sender, e) => {
					if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.LogError($"[Python Server Error] {e.Data}");
				};
				_pythonProcess.Start();
				_pythonProcess.BeginOutputReadLine();
				_pythonProcess.BeginErrorReadLine();
				_isServerRunning = true;
				UnityEngine.Debug.Log($"[Addon_MGR] Python server started on port {_serverPort}");
			} catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to start Python server: {e.Message}");
			}
#else
			UnityEngine.Debug.LogWarning("[Addon_MGR] Python server auto-start not supported on this platform in build; start addon server manually.");
#endif
		}
		
		IEnumerator RequestLoadEnabledAddonsAfterDelay() {
			UnityEngine.Debug.Log("[Addon_MGR] Auto-loading enabled addons in 2.5s (spin loading)...");
			yield return new WaitForSeconds(2.5f);
			int count = 0;
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value.isEnabled) {
					count++;
					UnityEngine.Debug.Log($"[Addon_MGR] Auto-load addon {count}: {kvp.Key}");
					yield return RequestLoadAddon(kvp.Key);
				}
			}
			if (count > 0)
				UnityEngine.Debug.Log($"[Addon_MGR] Auto-load finished. Requested {count} addon(s).");
		}

		/// <summary>Call from Add-on Manager \"Load addons now\" button. Requests Python to load all enabled addons.</summary>
		public void RequestLoadAllEnabledAddonsNow(Action onComplete = null) {
			StartCoroutine(RequestLoadAllEnabledAddonsNowCrtn(onComplete));
		}

		IEnumerator RequestLoadAllEnabledAddonsNowCrtn(Action onComplete) {
			int count = 0;
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value.isEnabled) {
					count++;
					UnityEngine.Debug.Log($"[Addon_MGR] Load addons now: requesting addon {count}: {kvp.Key}");
					yield return RequestLoadAddon(kvp.Key);
				}
			}
			UnityEngine.Debug.Log($"[Addon_MGR] Load addons now finished. Requested {count} addon(s). Check [Addon_SocketServer] and [AddonUI_MGR] logs to see if create_panel ran.");
			AddonDebugCapture.MarkLoadAddonsFinished();
			onComplete?.Invoke();
		}
		
		/// <summary>Polls GET /ready until Python has connected to Unity (socket 5555), so create_panel works when we POST /load_addon. Same pattern as SD: Unity is HTTP client, confirms connection by getting a successful response.</summary>
		IEnumerator WaitForAddonServerReady(int maxAttempts = 60, float interval = 0.5f) {
			if (!_isServerRunning) {
				UnityEngine.Debug.LogWarning("[Addon_MGR] Python server not running; attempting to start it now...");
				StartPythonServer();
				if (!_isServerRunning) {
					UnityEngine.Debug.LogError("[Addon_MGR] Could not start Python server. Check python is installed and on PATH. Addon load aborted.");
					yield break;
				}
				yield return new WaitForSeconds(2f);
			}

			string readyUrl = $"http://127.0.0.1:{_httpServerPort}/ready";
			bool loggedHttpReachable = false;
			int consecutiveConnectionErrors = 0;
			for (int i = 0; i < maxAttempts; i++) {
				using (var req = new UnityWebRequest(readyUrl)) {
					req.downloadHandler = new DownloadHandlerBuffer();
					yield return req.SendWebRequest();
					if (req.result == UnityWebRequest.Result.Success) {
						consecutiveConnectionErrors = 0;
						if (!loggedHttpReachable) {
							loggedHttpReachable = true;
							UnityEngine.Debug.Log($"[Addon_MGR] Addon HTTP server (FastAPI) responding on port {_httpServerPort} (Unity connected to FastAPI, same role as Unity→SD).");
						}
						try {
							var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
							if (json["ready"]?.Value<bool>() == true) {
								UnityEngine.Debug.Log("[Addon_MGR] Addon server ready (Python connected to Unity socket 5555).");
								yield break;
							}
						} catch { }
					} else {
						consecutiveConnectionErrors++;
						if (consecutiveConnectionErrors >= 10 && !loggedHttpReachable) {
							UnityEngine.Debug.LogWarning($"[Addon_MGR] Cannot reach Python HTTP server after {consecutiveConnectionErrors} attempts. Is Python running? Error: {req.error}");
						}
					}
				}
				yield return new WaitForSeconds(interval);
			}
			UnityEngine.Debug.LogWarning("[Addon_MGR] Addon server /ready did not become true within timeout. Check: (1) Is Python running? (2) Does Player.log show [Addon_SocketServer] Started listening on 127.0.0.1:5555?");
		}

		IEnumerator RequestLoadAddon(string addonId) {
			yield return WaitForAddonServerReady();
			UnityEngine.Debug.Log($"[Addon_MGR] Sending load request to Python for: {addonId}");
			string url = $"http://127.0.0.1:{_httpServerPort}/load_addon";
			string body = "{\"addon_id\":\"" + JsonEscape(addonId) + "\"}";
			using (var req = new UnityWebRequest(url, "POST")) {
				req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				yield return req.SendWebRequest();
				if (req.result != UnityWebRequest.Result.Success) {
					UnityEngine.Debug.LogError($"[Addon_MGR] load_addon failed for {addonId}: {req.error}. Ensure Python server is running on port {_httpServerPort}");
					MarkAddonLoadFailed(addonId);
					yield break;
				}
				bool loadSucceeded = false;
				try {
					var json = JObject.Parse(req.downloadHandler?.text ?? "{}");
					loadSucceeded = json["success"]?.Value<bool>() ?? false;
				} catch {
					// Response not valid JSON or missing success
				}
				string responseBody = req.downloadHandler?.text ?? "";
				if (loadSucceeded) {
					UnityEngine.Debug.Log($"[Addon_MGR] Successfully loaded addon: {addonId}. Response: {responseBody}");
				} else {
					UnityEngine.Debug.LogError($"[Addon_MGR] Python reported addon load failure for {addonId}. Raw response: {responseBody}. Check Python console for register()/socket errors.");
					MarkAddonLoadFailed(addonId);
				}
			}
		}

		/// <summary>When Python reports load failure, keep addon disabled so UI state matches (no stale "enabled" for broken addons).</summary>
		void MarkAddonLoadFailed(string addonId) {
			if (_registeredAddons.TryGetValue(addonId, out var addon)) {
				addon.isEnabled = false;
				OnAddonEnabledStateChanged?.Invoke(addonId);
			}
		}

		/// <summary>StreamingAssets add-on id for on-screen full view ribbon dock (matches folder name and Python <c>ADDON_ID</c>).</summary>
		public const string RibbonOnlyFullscreenAddonId = "RibbonOnlyFullscreen";

		bool IsAddonEnabled(string addonId) {
			if (string.IsNullOrEmpty(addonId) || _registeredAddons == null) {
				return false;
			}
			return _registeredAddons.TryGetValue(addonId, out var info) && info != null && info.isEnabled;
		}

		/// <summary>
		/// <see cref="RibbonOnlyFullscreenAddonId"/>: run <c>spz.ui.attach_viewport_fullview_toggle</c> from Unity on the main thread
		/// until the Gen Art column dock is visible. Does not use the right command-ribbon tab strip; add-on is driven from <see cref="EnableAddon"/> only.
		/// When HTTP is off, Python <c>register()</c> may not run, so this path is required.
		/// </summary>
		IEnumerator CoEnsureRibbonOnlyFullscreenViewportDock() {
			yield return null;
			const int maxFrames = 600;
			for (int f = 0; f < maxFrames; f++) {
				if (this == null) {
					yield break;
				}
				if (!IsAddonEnabled(RibbonOnlyFullscreenAddonId)) {
					yield break;
				}
				Addon_SocketServer.TryAttachViewportFullViewToggleFromCore(null);
				if (RibbonViewportFullViewOnScreen_Toggle_UI.IsAnyVisibleBuiltDock()) {
					UnityEngine.Debug.Log(
						"[Addon_MGR] RibbonOnlyFullscreen: viewport FULL/SRN dock next to Gen Art is visible (add-on manager path).");
					yield break;
				}
				yield return null;
			}
			UnityEngine.Debug.LogWarning(
				"[Addon_MGR] RibbonOnlyFullscreen: no visible viewport dock after "
				+ maxFrames
				+ " frames. Use Play with the main scene, SD/Gen Art UI loaded, and enable the add-on. If HTTP is on, check Python <c>register()</c> and the Console for attach errors.");
		}
		
		/// <summary>
		/// Registers UI elements created by an add-on
		/// </summary>
		public void RegisterAddonUI(string addonId, GameObject uiElement) {
			if (_registeredAddons.ContainsKey(addonId)) {
				_registeredAddons[addonId].uiElements.Add(uiElement);
			}
		}
		
		/// <summary>
		/// Unloads an add-on and destroys its UI elements (panel content, ribbon tab, and clears all registries).
		/// </summary>
		public void UnloadAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) return;
			
			var addon = _registeredAddons[addonId];
			addon.isEnabled = false;

			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				RibbonViewportFullViewOnScreen_Toggle_UI.TeardownAllDocksForAddonDisabled();
			}
			
			// 1) AddonUI_MGR: destroy panel content + buttons and clear its state (callbacks, element refs)
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.DestroyAddonUI(addonId);
			
			// 2) CommandRibbon_UI: remove addon tab and panel (same resolution as AddonRibbonIntegration.ResolveCommandRibbon)
			var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
			if (ribbon != null)
				ribbon.RemoveAddonPanel(addonId);
			
			addon.uiElements.Clear();
			
			UnityEngine.Debug.Log($"[Addon_MGR] Unloaded add-on: {addonId}");
		}
		
		static string JsonEscape(string s) {
			if (string.IsNullOrEmpty(s)) return "";
			return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
		}
		
		/// <summary>
		/// Gets list of discovered add-ons
		/// </summary>
		public IReadOnlyDictionary<string, AddonInfo> GetAddons() {
			return _registeredAddons;
		}

		/// <summary>
		/// The Gen Art column dock may live on a host that is disabled when switching right-panel tabs. Call from
		/// <see cref="RibbonViewportFullViewOnScreen_Toggle_UI.OnDisable"/> to decide whether to remove injected UI:
		/// only when <see cref="RibbonOnlyFullscreenAddonId"/> is <b>off</b> in the manager, not on every parent deactivate.
		/// </summary>
		public static bool ShouldTearDownViewportFullViewDockOnHostDisabled() {
			if (instance == null) {
				return false;
			}
			if (!instance._registeredAddons.TryGetValue(RibbonOnlyFullscreenAddonId, out var info) || info == null) {
				return false;
			}
			return !info.isEnabled;
		}
		
		/// <summary>
		/// Enables an add-on and requests Python server to load it (so panel appears).
		/// </summary>
		public void EnableAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Add-on '{addonId}' not found");
				return;
			}
			
			_registeredAddons[addonId].isEnabled = true;
			UnityEngine.Debug.Log($"[Addon_MGR] Enabled add-on: {addonId}");
			if (_enableHttpServer) {
				StartCoroutine(RequestLoadAddon(addonId));
			}
			else {
				UnityEngine.Debug.LogWarning(
					"[Addon_MGR] Add-on HTTP is disabled: Python will not run register().");
			}
			// On-screen full view: must run from Unity (Python register may never run, or may race). No command-ribbon tab.
			if (string.Equals(addonId, RibbonOnlyFullscreenAddonId, StringComparison.Ordinal)) {
				var ribbon = AddonRibbonIntegration.ResolveCommandRibbon();
				if (ribbon != null) {
					ribbon.RemoveAddonPanel(addonId);
				}
				StartCoroutine(CoEnsureRibbonOnlyFullscreenViewportDock());
			}
		}
		
		/// <summary>
		/// Disables an add-on (unloads it)
		/// </summary>
		public void DisableAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Add-on '{addonId}' not found");
				return;
			}
			
			UnloadAddon(addonId);
		}
		
		/// <summary>
		/// Forces a re-scan of the Addons directory. Preserves enabled state for addons that are still present.
		/// </summary>
		public void RefreshAddons() {
			DiscoverAddons();
		}
		
		/// <summary>
		/// Gets the server port
		/// </summary>
		public int GetServerPort() {
			return _serverPort;
		}
		
		public int GetHttpServerPort() {
			return _httpServerPort;
		}
		
		public int GetWebSocketPort() {
			return _webSocketPort;
		}
		
		public bool IsHttpServerEnabled() {
			return _enableHttpServer;
		}
		
		public bool IsWebSocketServerEnabled() {
			return _enableWebSocketServer;
		}
		
		void OnDestroy() {
			Application.quitting -= HandleApplicationQuitting;
			ShutdownAddonApiBeforeQuit();
			if (instance == this)
				instance = null;
		}
	}
}
