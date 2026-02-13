using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using System.Diagnostics;

namespace spz {

	/// <summary>
	/// Manages add-on discovery, lifecycle, and Python server process.
	/// </summary>
	public class Addon_MGR : MonoBehaviour {
		public static Addon_MGR instance { get; private set; }
		
		[SerializeField] string _pythonServerScript = "addon_server.py";
		[SerializeField] int _serverPort = 5555;
		[SerializeField] int _httpServerPort = 5557;
		[SerializeField] int _webSocketPort = 5558;
		[SerializeField] bool _enableHttpServer = true; // FastAPI in Python (recommended)
		[SerializeField] bool _enableCSharpHttpServer = false; // Legacy C# HttpListener (deprecated)
		[SerializeField] bool _enableWebSocketServer = false;
		
		private Process _pythonProcess;
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
		}
		
		void Start() {
			StartCoroutine(InitializeAddonSystem());
		}
		
		IEnumerator InitializeAddonSystem() {
			// Wait for FastPath_API to be ready
			while (FastPath_API.instance == null || !FastPath_API.instance.IsReady()) {
				yield return null;
			}
			
			// Discover add-ons
			DiscoverAddons();
			
			// Start Python server (includes FastAPI HTTP server if enabled)
			StartPythonServer();
			
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
							// Update existing or add new; preserve enabled state when re-discovering
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
		/// Starts the Python server process
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
			
			try {
				string arguments = $"\"{serverScriptPath}\" --port {_serverPort}";
				if (_enableHttpServer) {
					arguments += $" --http-port {_httpServerPort}";
				} else {
					arguments += " --no-http";
				}
				
				_pythonProcess = new Process {
					StartInfo = new ProcessStartInfo {
						FileName = "python",
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
		}
		
		IEnumerator RequestLoadEnabledAddonsAfterDelay() {
			yield return new WaitForSeconds(2.5f);
			foreach (var kvp in _registeredAddons) {
				if (kvp.Value.isEnabled) {
					yield return RequestLoadAddon(kvp.Key);
				}
			}
		}
		
		IEnumerator RequestLoadAddon(string addonId) {
			string url = $"http://127.0.0.1:{_httpServerPort}/load_addon";
			string body = "{\"addon_id\":\"" + addonId + "\"}";
			using (var req = new UnityWebRequest(url, "POST")) {
				req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
				req.downloadHandler = new DownloadHandlerBuffer();
				req.SetRequestHeader("Content-Type", "application/json");
				yield return req.SendWebRequest();
				if (req.result == UnityWebRequest.Result.Success) {
					UnityEngine.Debug.Log($"[Addon_MGR] Requested load addon: {addonId}");
				} else {
					UnityEngine.Debug.LogWarning($"[Addon_MGR] load_addon failed for {addonId}: {req.error}");
				}
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
		/// Unloads an add-on and destroys its UI elements
		/// </summary>
		public void UnloadAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) return;
			
			var addon = _registeredAddons[addonId];
			addon.isEnabled = false;
			
			// Destroy all UI elements
			foreach (var uiElement in addon.uiElements) {
				if (uiElement != null) {
					Destroy(uiElement);
				}
			}
			addon.uiElements.Clear();
			
			UnityEngine.Debug.Log($"[Addon_MGR] Unloaded add-on: {addonId}");
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
