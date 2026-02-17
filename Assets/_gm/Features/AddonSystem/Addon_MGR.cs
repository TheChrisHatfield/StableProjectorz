using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using Lavender.Systems;
#endif

namespace spz {

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
		
		private Process _pythonProcess;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
		private uint _pythonServerPid; // When started via StartExternalProcess (IL2CPP-safe path)
#endif
		private Dictionary<string, AddonInfo> _registeredAddons = new Dictionary<string, AddonInfo>();
		private bool _isServerRunning = false;
		
		public class AddonInfo {
			public string id;
			public string path;
			public bool isEnabled;
			public List<GameObject> uiElements = new List<GameObject>();
		}
		
		void Awake() {
			if (instance != null) { DestroyImmediate(this); return; }
			instance = this;
			if (GetComponent<AddonDebugCapture>() == null)
				gameObject.AddComponent<AddonDebugCapture>();
			if (GetComponent<Launch_Addons_Bat_File>() == null)
				gameObject.AddComponent<Launch_Addons_Bat_File>();
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
			if (!_isServerRunning && _enableHttpServer && HasAnyEnabledAddon() && !WasLaunchedByAddonsBat() && !Application.isEditor) {
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
				string batContent = "@echo off\r\ncd /d \"" + workDir + "\"\r\n\"" + pythonExe.Replace("\"", "\"\"") + "\" \"" + serverScriptPath.Replace("\"", "\"\"") + "\" --port " + _serverPort + " --addons-dir \"" + addonsPath.Replace("\"", "\"\"") + "\" " + httpArg + "\r\n";
				File.WriteAllText(batPath, batContent);
				UnityEngine.Debug.Log("[Addon_MGR] Addon server console will open; any Python errors (e.g. missing uvicorn/fastapi) appear there.");
				uint pid = StartExternalProcess.Run_Bat_or_Shortcut_or_Command(batPath, isJustFile: true, workDir, keepWindow: true, hidden: false, attachToConsole: false);
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
#else
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
					if (!string.IsNullOrEmpty(e.Data)) {
						UnityEngine.Debug.Log($"[Python Server] {e.Data}");
					}
				};
				
				_pythonProcess.ErrorDataReceived += (sender, e) => {
					if (!string.IsNullOrEmpty(e.Data)) {
						UnityEngine.Debug.LogError($"[Python Server Error] {e.Data}");
					}
				};
				
				_pythonProcess.Start();
				_pythonProcess.BeginOutputReadLine();
				_pythonProcess.BeginErrorReadLine();
				
				_isServerRunning = true;
				UnityEngine.Debug.Log($"[Addon_MGR] Python server started on port {_serverPort}");
			}
			catch (Exception e) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Failed to start Python server: {e.Message}");
			}
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
			string readyUrl = $"http://127.0.0.1:{_httpServerPort}/ready";
			bool loggedHttpReachable = false;
			for (int i = 0; i < maxAttempts; i++) {
				using (var req = new UnityWebRequest(readyUrl)) {
					req.downloadHandler = new DownloadHandlerBuffer();
					yield return req.SendWebRequest();
					if (req.result == UnityWebRequest.Result.Success) {
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
			
			// 1) AddonUI_MGR: destroy panel content + buttons and clear its state (callbacks, element refs)
			if (AddonUI_MGR.instance != null)
				AddonUI_MGR.instance.DestroyAddonUI(addonId);
			
			// 2) CommandRibbon_UI: remove addon tab and panel container from the ribbon (find ribbon even if inactive, same as CreatePanel)
			var ribbon = CommandRibbon_UI.instance ?? UnityEngine.Object.FindObjectOfType<CommandRibbon_UI>(true);
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
			if (_pythonProcess != null) {
				// Cancel async read operations to prevent event handlers from firing
				try {
					_pythonProcess.CancelOutputRead();
					_pythonProcess.CancelErrorRead();
				} catch {
					// Already cancelled or process exited, ignore
				}
				
				if (!_pythonProcess.HasExited) {
					try {
						_pythonProcess.Kill();
					} catch {
						// Process may have already exited, ignore
					}
				}
				
				try {
					_pythonProcess.Dispose();
				} catch {
					// Already disposed, ignore
				}
				_pythonProcess = null;
			}
		}
	}
}
