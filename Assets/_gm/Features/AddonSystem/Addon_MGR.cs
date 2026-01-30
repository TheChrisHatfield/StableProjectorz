using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
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
		[SerializeField] bool _enableHttpServer = true;
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
			
			// Start Python server
			StartPythonServer();
			
			// Start HTTP server if enabled
			if (_enableHttpServer && Addon_HttpServer.instance == null) {
				GameObject httpServerObj = new GameObject("Addon_HttpServer");
				httpServerObj.AddComponent<Addon_HttpServer>();
			}
		}
		
		/// <summary>
		/// Scans StreamingAssets/Addons/ for add-on directories
		/// </summary>
		void DiscoverAddons() {
			string addonsPath = Path.Combine(Application.streamingAssetsPath, "Addons");
			
			if (!Directory.Exists(addonsPath)) {
				Directory.CreateDirectory(addonsPath);
				UnityEngine.Debug.Log($"[Addon_MGR] Created Addons directory at {addonsPath}");
				return;
			}
			
			var addonDirs = Directory.GetDirectories(addonsPath);
			foreach (var dir in addonDirs) {
				string initFile = Path.Combine(dir, "__init__.py");
				if (File.Exists(initFile)) {
					string addonId = Path.GetFileName(dir);
					if (!_registeredAddons.ContainsKey(addonId)) {
						_registeredAddons[addonId] = new AddonInfo {
							id = addonId,
							path = dir,
							isEnabled = false
						};
						UnityEngine.Debug.Log($"[Addon_MGR] Discovered add-on: {addonId}");
					}
				}
			}
		}
		
		/// <summary>
		/// Starts the Python server process
		/// </summary>
		void StartPythonServer() {
			if (_isServerRunning) return;
			
			string serverScriptPath = Path.Combine(Application.streamingAssetsPath, "AddonSystem", _pythonServerScript);
			
			if (!File.Exists(serverScriptPath)) {
				UnityEngine.Debug.LogError($"[Addon_MGR] Python server script not found at {serverScriptPath}");
				return;
			}
			
			try {
				_pythonProcess = new Process {
					StartInfo = new ProcessStartInfo {
						FileName = "python",
						Arguments = $"\"{serverScriptPath}\" --port {_serverPort}",
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
		/// Enables an add-on (loads it via Python server)
		/// </summary>
		public void EnableAddon(string addonId) {
			if (!_registeredAddons.ContainsKey(addonId)) {
				UnityEngine.Debug.LogWarning($"[Addon_MGR] Add-on '{addonId}' not found");
				return;
			}
			
			_registeredAddons[addonId].isEnabled = true;
			UnityEngine.Debug.Log($"[Addon_MGR] Enabled add-on: {addonId}");
			// Note: Actual loading happens via Python server when it calls register()
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
		/// Forces a re-scan of the Addons directory
		/// </summary>
		public void RefreshAddons() {
			_registeredAddons.Clear();
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
			if (_pythonProcess != null && !_pythonProcess.HasExited) {
				_pythonProcess.Kill();
				_pythonProcess.Dispose();
			}
		}
	}
}
